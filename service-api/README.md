# 窗巡授权服务

这是窗巡客户端的远程授权校验服务示例实现。服务使用和客户端一致的 `WML1.` 加密格式返回响应，每次响应都会使用新的随机数加密，客户端会校验响应里的请求随机数，避免伪造固定返回内容。

## 启动

```powershell
cd service-api
$env:LICENSE_ADDR=":8081"
$env:LICENSE_CRYPTO_KEY_BASE64="MDEyMzQ1Njc4OUFCQ0RFRjAxMjM0NTY3ODlBQkNERUY="
go run .
```

## 配置

- `LICENSE_ADDR`：监听地址，默认 `:8081`。
- `LICENSE_CRYPTO_KEY_BASE64`：授权码和响应加密密钥，必须和客户端打包时的密钥一致。
- `LICENSE_REVOKED_IDS`：已吊销授权 ID，多个 ID 用英文逗号分隔。

## 接口

`POST /license`

请求示例：

```json
{
  "licenseCode": "WML1.xxxxx",
  "machineCode": "当前机器码",
  "nonce": "客户端随机数",
  "clientVersion": "0.1.0",
  "product": "窗巡"
}
```

响应示例：

```json
{
  "response": "WML1.xxxxx"
}
```

解密后的响应字段：

```json
{
  "nonce": "客户端随机数",
  "serverUtc": "2026-06-11T10:00:00Z",
  "valid": true,
  "revoked": false,
  "expiresAt": "2027-06-11T00:00:00Z",
  "message": "授权有效。"
}
```
