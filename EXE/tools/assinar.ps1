<#
.SYNOPSIS
  Assina um arquivo com o certificado de code signing do SkyAPI.

.DESCRIPTION
  O Windows desta maquina roda com o Smart App Control ligado, que bloqueia binario
  novo e desconhecido: um executavel recem compilado nao abre ate ser assinado por um
  certificado em que a maquina confia (erro 0x800711C7, "politica de Controle de
  Aplicativo bloqueou este arquivo").

  ATENCAO: o certificado padrao usado aqui e AUTOASSINADO. Ele resolve nesta maquina,
  onde esta instalado como raiz confiavel, e em qualquer maquina onde voce instale a
  mesma raiz. NAO serve para distribuir a clientes: no computador deles o Windows nao
  conhece essa raiz, o SmartScreen avisa "editor desconhecido" e o Smart App Control
  bloqueia do mesmo jeito. Para distribuir, assine com um certificado de code signing
  publicamente confiavel (OV ou, de preferencia, EV) e informe o thumbprint dele.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\assinar.ps1 -Arquivo SkyAPI-1.1.12.exe
#>
param(
    [Parameter(Mandatory = $true)][string]$Arquivo,
    [string]$Thumbprint = '780351A6E25EA8DF163FE5CF083F86E31DD9C38E',
    [string]$CarimboDeTempo = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Arquivo)) {
    Write-Host "Arquivo nao encontrado: $Arquivo" -ForegroundColor Red
    exit 1
}

$cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -eq $Thumbprint -and $_.HasPrivateKey } |
    Select-Object -First 1

if (-not $cert) {
    Write-Host "Certificado $Thumbprint nao encontrado com chave privada." -ForegroundColor Yellow
    Write-Host "O arquivo fica sem assinatura. Com o Smart App Control ligado, ele nao vai abrir."
    exit 2
}

# O carimbo de tempo mantem a assinatura valida depois que o certificado expira.
# Sem rede, assina mesmo assim e avisa: e melhor uma assinatura sem carimbo do que nenhuma.
try {
    $r = Set-AuthenticodeSignature -FilePath $Arquivo -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $CarimboDeTempo
} catch {
    Write-Host "Carimbo de tempo indisponivel; assinando sem ele." -ForegroundColor Yellow
    $r = Set-AuthenticodeSignature -FilePath $Arquivo -Certificate $cert -HashAlgorithm SHA256
}

if ($r.Status -ne 'Valid') {
    Write-Host "Falha ao assinar: $($r.Status) - $($r.StatusMessage)" -ForegroundColor Red
    exit 1
}

Write-Host "Assinado: $Arquivo  ($($cert.Subject))"
if (-not $r.TimeStamperCertificate) { Write-Host "Sem carimbo de tempo." -ForegroundColor Yellow }
exit 0
