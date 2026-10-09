# Instalação do Aura Ice Controller

O pacote recomendado para usuários finais é `AuraIce-Setup.exe`, um único instalador com assistente. Ele instala:

- o driver oficial PawnIO, se não houver uma versão compatível instalada;
- `AuraIce.Service.exe`, como serviço automático do Windows;
- `AuraIce.App.exe`, iniciando minimizado na bandeja do sistema;
- atalhos para abrir e desinstalar o Aura Ice Controller no menu Iniciar;
- `%ProgramData%\AuraIce`, para estado e configuração.

Clique no ícone do Aura Ice na bandeja para abrir o widget de temperatura. O widget atualiza a leitura automaticamente e permite ajustar o intervalo. Para remover, pesquise por “Desinstalar Aura Ice Controller” no menu Iniciar ou escolha a opção de desinstalação no menu do ícone da bandeja. O PawnIO não é removido, pois outros aplicativos podem depender dele.

O instalador precisa de conexão com a Internet somente quando precisa baixar o PawnIO. O download vem da página oficial de releases, usa versão fixa e é validado por hash SHA-256 e assinatura Authenticode antes de entrar no processo de build. No computador do usuário, o Burn também valida o hash SHA-512 esperado antes de executar o pacote.

O PawnIO é um driver de kernel de terceiros, assinado pelo fornecedor e usado pelo LibreHardwareMonitor para acessar sensores internos. O Windows solicita elevação administrativa. O Aura Ice não desativa Smart App Control, Secure Boot, Integridade da memória ou outras proteções. A remoção do Aura Ice não remove o PawnIO, pois outros aplicativos podem depender dele.

## Gerar o pacote

Em um Windows com .NET SDK e acesso à Internet:

```powershell
./scripts/build-installer.ps1
```

O resultado recomendado para o usuário final será `installer/bin/Release/AuraIce-Setup.exe`. O MSI interno fica em `installer/bin/Release/pt-BR/` para diagnóstico e implantação avançada.

Para recompilar os instaladores sem republicar os binários:

```powershell
./scripts/build-installer.ps1 -SkipPublish
```

O `AuraIce-Setup.exe` mostra uma tela inicial em português e instala o PawnIO antes do serviço. Ele pede autorização administrativa pelo Windows; se o driver pedir reinicialização, conclua-a para ativar a leitura da CPU. A atualização do MSI usa `MajorUpgrade` e preserva a configuração em `ProgramData`.

Para usuários finais, basta executar `AuraIce-Setup.exe` e seguir as instruções. Não é necessário abrir PowerShell ou instalar o LibreHardwareMonitor manualmente.

## Instalação de desenvolvimento

O fluxo sem MSI continua disponível:

```powershell
./scripts/publish.ps1
./scripts/install-dev.ps1
./scripts/configure-service-recovery.ps1
```

O script de recuperação reinicia o serviço após falhas transitórias, com atraso de 60 segundos na primeira tentativa e 5 minutos na segunda.
