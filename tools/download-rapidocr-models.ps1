param(
    [string]$Destination = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $root "artifacts\rapidocr-models\v5"
}

$models = @(
    @{
        Name = "ch_PP-OCRv5_det_mobile.onnx"
        Url = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.8.0/onnx/PP-OCRv5/det/ch_PP-OCRv5_det_mobile.onnx"
        Sha256 = "4d97c44a20d30a81aad087d6a396b08f786c4635742afc391f6621f5c6ae78ae"
    },
    @{
        Name = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"
        Url = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.8.0/onnx/PP-OCRv5/cls/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"
        Sha256 = "54379ae5174d026780215fc748a7f31910dee36818e63d49e17dc598ecc82df7"
    },
    @{
        Name = "ch_PP-OCRv5_rec_mobile.onnx"
        Url = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.8.0/onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile.onnx"
        Sha256 = "5825fc7ebf84ae7a412be049820b4d86d77620f204a041697b0494669b1742c5"
    },
    @{
        Name = "ppocrv5_dict.txt"
        Url = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.8.0/paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile/ppocrv5_dict.txt"
        Sha256 = ""
    }
)

New-Item -ItemType Directory -Path $Destination -Force | Out-Null

foreach ($model in $models) {
    $path = Join-Path $Destination $model.Name
    $needsDownload = -not (Test-Path $path)
    if (-not $needsDownload -and -not [string]::IsNullOrWhiteSpace($model.Sha256)) {
        $hash = (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $needsDownload = $hash -ne $model.Sha256
    }

    if ($needsDownload) {
        Write-Host "Downloading RapidOCR model: $($model.Name)" -ForegroundColor Cyan
        Invoke-WebRequest -UseBasicParsing -Uri $model.Url -OutFile $path
    }

    if (-not [string]::IsNullOrWhiteSpace($model.Sha256)) {
        $hash = (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $model.Sha256) {
            Remove-Item -LiteralPath $path -Force
            throw "RapidOCR model checksum mismatch: $($model.Name)"
        }
    }
}

Write-Host "RapidOCR models are ready: $Destination" -ForegroundColor Green
