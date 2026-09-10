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
  powershell -ExecutionPolicy Bypass -File tools/assinar.ps1 -Arquivo SkyAPI-1.1.12.exe
#>
param(
    [Parameter(Mandatory = $true)][string]$Arquivo,
    [string]$Thumbprint = '780351A6E25EA8DF163FE5CF083F86E31DD9C38E',
    [string]$CarimboDeTempo = 'http://timestamp.digicert.com'
)

# Sem try/catch, um erro terminante aqui NAO vira codigo de saida diferente de zero, e o
# compilar.cmd acreditaria que assinou. Todo caminho de falha abaixo termina em "exit 1".
try {
    if (-not (Test-Path -LiteralPath $Arquivo)) {
        Write-Host "Arquivo nao encontrado: $Arquivo" -ForegroundColor Red
        exit 1
    }
    $Arquivo = (Resolve-Path -LiteralPath $Arquivo).Path

    $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $Thumbprint -and $_.HasPrivateKey } |
        Select-Object -First 1

    if (-not $cert) {
        Write-Host "Certificado $Thumbprint nao encontrado com chave privada." -ForegroundColor Red
        Write-Host "O arquivo fica sem assinatura e, com o Smart App Control ligado, nao vai abrir."
        exit 1
    }

    # Um executavel de 162 MB recem copiado costuma ficar preso alguns segundos enquanto o
    # antivirus o varre. Falhar de primeira aqui deixaria o binario sem assinatura.
    $tentativas = 6
    $assinatura = $null
    for ($i = 1; $i -le $tentativas; $i++) {
        try {
            # O carimbo de tempo mantem a assinatura valida depois que o certificado expira.
            $assinatura = Set-AuthenticodeSignature -FilePath $Arquivo -Certificate $cert `
                -HashAlgorithm SHA256 -TimestampServer $CarimboDeTempo -ErrorAction Stop
            break
        } catch [System.IO.IOException] {
            if ($i -eq $tentativas) { throw }
            Write-Host "Arquivo em uso; nova tentativa em 3s ($i/$tentativas)." -ForegroundColor Yellow
            Start-Sleep -Seconds 3
        } catch {
            # Servidor de carimbo fora do ar: melhor assinar sem ele do que nao assinar.
            Write-Host "Carimbo de tempo indisponivel; assinando sem ele." -ForegroundColor Yellow
            $assinatura = Set-AuthenticodeSignature -FilePath $Arquivo -Certificate $cert `
                -HashAlgorithm SHA256 -ErrorAction Stop
            break
        }
    }

    # Conferencia independente: o que vale e o que o Windows le do arquivo depois de gravado.
    $final = Get-AuthenticodeSignature -LiteralPath $Arquivo
    if ($final.Status -ne 'Valid') {
        Write-Host "Falha ao assinar: $($final.Status) - $($final.StatusMessage)" -ForegroundColor Red
        exit 1
    }

    Write-Host "Assinado: $Arquivo  ($($cert.Subject))"
    if (-not $final.TimeStamperCertificate) {
        Write-Host "AVISO: assinado sem carimbo de tempo; a assinatura deixa de valer quando o certificado expirar." -ForegroundColor Yellow
    }
    exit 0

} catch {
    Write-Host "Falha ao assinar: $($_.Exception.Message)" -ForegroundColor Red
    # Causa mais comum: o proprio aplicativo esta aberto e segura o arquivo.
    $nome = [System.IO.Path]::GetFileNameWithoutExtension($Arquivo)
    $preso = Get-Process -Name $nome -ErrorAction SilentlyContinue
    if ($preso) {
        Write-Host "O processo '$nome' (PID $($preso.Id -join ', ')) esta com o arquivo aberto." -ForegroundColor Yellow
        Write-Host "Feche o aplicativo e rode de novo."
    }
    exit 1
}
