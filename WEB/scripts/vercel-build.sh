#!/usr/bin/env bash
# Compila o SkyAPI Web na Vercel.
#
# A imagem de build da Vercel não traz o .NET, então o SDK 8 é baixado para .dotnet/
# dentro do próprio build. O resultado estático fica em dist/, que é o outputDirectory
# declarado em vercel.json. As funções de api/ são publicadas pela Vercel à parte.
set -euo pipefail

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
# A imagem pode não ter ICU. O build não depende de cultura; o aplicativo formata em pt-BR no navegador.
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

DOTNET_ROOT="$ROOT/.dotnet"
export DOTNET_ROOT
if [ ! -x "$DOTNET_ROOT/dotnet" ]; then
  echo "Baixando o SDK .NET 8…"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$ROOT/dotnet-install.sh"
  bash "$ROOT/dotnet-install.sh" --channel 8.0 --install-dir "$DOTNET_ROOT" --no-path
  rm -f "$ROOT/dotnet-install.sh"
fi
export PATH="$DOTNET_ROOT:$PATH"

dotnet --version

rm -rf "$ROOT/publish" "$ROOT/dist"
dotnet publish Client/SkyAPI.Web.csproj -c Release -o "$ROOT/publish"
mv "$ROOT/publish/wwwroot" "$ROOT/dist"
rm -rf "$ROOT/publish"

echo "Publicado em dist/:"
ls -1 "$ROOT/dist"
