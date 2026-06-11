package main

import (
	"crypto/aes"
	"crypto/cipher"
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"errors"
	"log"
	"net/http"
	"os"
	"strings"
	"time"
)

const (
	tokenPrefix       = "WML1."
	nonceSize         = 12
	tagSize           = 16
	defaultListenAddr = ":8081"
	defaultKeyBase64  = "MDEyMzQ1Njc4OUFCQ0RFRjAxMjM0NTY3ODlBQkNERUY="
)

type server struct {
	key        []byte
	revokedIDs map[string]struct{}
}

type licenseRequest struct {
	LicenseCode   string `json:"licenseCode"`
	MachineCode   string `json:"machineCode"`
	Nonce         string `json:"nonce"`
	ClientVersion string `json:"clientVersion"`
	Product       string `json:"product"`
}

type licensePayload struct {
	LicenseID   string     `json:"licenseId"`
	LicenseType string     `json:"licenseType"`
	Edition     string     `json:"edition"`
	DeviceHash  string     `json:"deviceHash"`
	Features    []string   `json:"features"`
	IssuedAt    *time.Time `json:"issuedAt"`
	ExpiresAt   *time.Time `json:"expiresAt"`
	Nonce       string     `json:"nonce"`
}

type licenseResponse struct {
	Nonce     string     `json:"nonce"`
	ServerUTC time.Time  `json:"serverUtc"`
	Valid     bool       `json:"valid"`
	Revoked   bool       `json:"revoked"`
	ExpiresAt *time.Time `json:"expiresAt,omitempty"`
	Message   string     `json:"message"`
}

func main() {
	key, err := loadCryptoKey()
	if err != nil {
		log.Fatalf("授权加密密钥配置错误：%v", err)
	}

	s := &server{
		key:        key,
		revokedIDs: parseRevokedIDs(os.Getenv("LICENSE_REVOKED_IDS")),
	}

	mux := http.NewServeMux()
	mux.HandleFunc("GET /healthz", s.health)
	mux.HandleFunc("POST /license", s.validateLicense)

	addr := envOrDefault("LICENSE_ADDR", defaultListenAddr)
	log.Printf("窗巡授权服务已启动，监听地址：%s", addr)
	if err := http.ListenAndServe(addr, mux); err != nil {
		log.Fatal(err)
	}
}

func (s *server) health(w http.ResponseWriter, _ *http.Request) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	_, _ = w.Write([]byte(`{"ok":true}`))
}

func (s *server) validateLicense(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		http.Error(w, "只支持 POST 请求。", http.StatusMethodNotAllowed)
		return
	}

	defer r.Body.Close()
	var req licenseRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		http.Error(w, "请求内容不是有效的 JSON。", http.StatusBadRequest)
		return
	}

	req.Nonce = strings.TrimSpace(req.Nonce)
	if req.Nonce == "" {
		http.Error(w, "请求随机数不能为空。", http.StatusBadRequest)
		return
	}

	payload, result := s.evaluate(req)
	if payload != nil {
		result.ExpiresAt = payload.ExpiresAt
	}

	encrypted, err := encryptJSON(result, s.key)
	if err != nil {
		http.Error(w, "授权响应加密失败。", http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	_ = json.NewEncoder(w).Encode(map[string]string{"response": encrypted})
}

func (s *server) evaluate(req licenseRequest) (*licensePayload, licenseResponse) {
	now := time.Now().UTC()
	response := licenseResponse{
		Nonce:     req.Nonce,
		ServerUTC: now,
		Valid:     false,
		Message:   "授权码无效。",
	}

	plain, err := decryptToken(req.LicenseCode, s.key)
	if err != nil {
		return nil, response
	}

	var payload licensePayload
	if err := json.Unmarshal(plain, &payload); err != nil {
		response.Message = "授权码内容无效。"
		return nil, response
	}

	if !strings.EqualFold(strings.TrimSpace(payload.DeviceHash), strings.TrimSpace(req.MachineCode)) {
		response.Message = "机器码不匹配。"
		return &payload, response
	}

	if _, ok := s.revokedIDs[payload.LicenseID]; ok && payload.LicenseID != "" {
		response.Revoked = true
		response.Message = "授权已吊销。"
		return &payload, response
	}

	if payload.ExpiresAt != nil && now.After(payload.ExpiresAt.UTC()) {
		response.Message = "授权已过期。"
		return &payload, response
	}

	response.Valid = true
	response.Message = "授权有效。"
	return &payload, response
}

func encryptJSON(value licenseResponse, key []byte) (string, error) {
	plain, err := json.Marshal(value)
	if err != nil {
		return "", err
	}

	block, err := aes.NewCipher(key)
	if err != nil {
		return "", err
	}

	gcm, err := cipher.NewGCM(block)
	if err != nil {
		return "", err
	}

	nonce := make([]byte, nonceSize)
	if _, err := rand.Read(nonce); err != nil {
		return "", err
	}

	sealed := gcm.Seal(nil, nonce, plain, nil)
	ciphertext := sealed[:len(sealed)-tagSize]
	tag := sealed[len(sealed)-tagSize:]
	payload := make([]byte, 0, len(nonce)+len(tag)+len(ciphertext))
	payload = append(payload, nonce...)
	payload = append(payload, tag...)
	payload = append(payload, ciphertext...)
	return tokenPrefix + base64.RawURLEncoding.EncodeToString(payload), nil
}

func decryptToken(token string, key []byte) ([]byte, error) {
	token = strings.TrimSpace(token)
	if !strings.HasPrefix(strings.ToUpper(token), tokenPrefix) {
		return nil, errors.New("授权码格式无效")
	}

	raw, err := base64.RawURLEncoding.DecodeString(token[len(tokenPrefix):])
	if err != nil {
		return nil, err
	}
	if len(raw) <= nonceSize+tagSize {
		return nil, errors.New("授权码内容无效")
	}

	block, err := aes.NewCipher(key)
	if err != nil {
		return nil, err
	}
	gcm, err := cipher.NewGCM(block)
	if err != nil {
		return nil, err
	}

	nonce := raw[:nonceSize]
	tag := raw[nonceSize : nonceSize+tagSize]
	ciphertext := raw[nonceSize+tagSize:]
	sealed := make([]byte, 0, len(ciphertext)+len(tag))
	sealed = append(sealed, ciphertext...)
	sealed = append(sealed, tag...)
	return gcm.Open(nil, nonce, sealed, nil)
}

func loadCryptoKey() ([]byte, error) {
	keyBase64 := envOrDefault("LICENSE_CRYPTO_KEY_BASE64", defaultKeyBase64)
	key, err := base64.StdEncoding.DecodeString(keyBase64)
	if err != nil {
		return nil, err
	}
	if len(key) != 16 && len(key) != 24 && len(key) != 32 {
		return nil, errors.New("密钥长度必须是 16、24 或 32 字节")
	}
	return key, nil
}

func parseRevokedIDs(value string) map[string]struct{} {
	result := map[string]struct{}{}
	for _, item := range strings.Split(value, ",") {
		id := strings.TrimSpace(item)
		if id != "" {
			result[id] = struct{}{}
		}
	}
	return result
}

func envOrDefault(name string, fallback string) string {
	value := strings.TrimSpace(os.Getenv(name))
	if value == "" {
		return fallback
	}
	return value
}
