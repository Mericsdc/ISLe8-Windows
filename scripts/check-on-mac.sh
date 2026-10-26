#!/usr/bin/env bash
# Mac'te derleme denetimi. ISLe8.Core'u derleyip testlerini çalıştırır, Windows uygulamasının
# bütün C# kodunu gerçek WinUI / Windows API'lerine karşı derler. Uygulama Mac'te çalışmaz;
# Windows'a özgü PRI ve MSIX paketleme adımları bu yüzden atlanır.
#
# Gerekenler: .NET 10 SDK (brew install --cask dotnet-sdk). DOTNET=/yol/dotnet ile başka bir
# kurulum gösterilebilir.
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET="${DOTNET:-dotnet}"

echo "== Core testleri"
"$DOTNET" test tests/ISLe8.Core.Tests --nologo -v q

echo "== Windows uygulaması (yalnızca derleme denetimi)"
"$DOTNET" build src/ISLe8.App --nologo -v q \
  -p:EnableWindowsTargeting=true \
  -p:AppxGeneratePriEnabled=false \
  -p:EnableMsixTooling=false \
  -p:WindowsPackageType=None \
  -p:Platform=x64
