# Plano AI-DOS — Rise Mode Aura Ice Black para Windows

## Resultado desejado

Um aplicativo instalável para Windows que leia a temperatura da CPU e a mantenha no visor do watercooler, com reconexão automática, estado persistente, interface gráfica e inicialização automática.

## Decisões

1. O MVP usa o driver HID nativo do Windows. Não haverá driver de kernel próprio enquanto o dispositivo continuar acessível como HID.
2. A leitura primária será feita pelo `LibreHardwareMonitorLib`, encapsulada atrás de uma interface para permitir outro provedor no futuro.
3. A lógica persistente ficará em um Windows Service; a interface será um aplicativo separado de bandeja.
4. O transporte, o protocolo e a telemetria serão projetos independentes para facilitar testes sem o hardware conectado.

## Evidências disponíveis

- O dispositivo é identificado como `VID 0xAA88 / PID 0x8666`.
- O transporte documentado é USB HID.
- O payload conhecido da temperatura possui 24 bytes de dados: inteiro no byte 0, primeiro decimal no byte 1 e preenchimento no restante. No Windows, o HidSharp também exige um byte de Report ID no início do relatório HID; para um dispositivo sem IDs, esse byte deve ser `0x00`.
- A atualização esperada é de aproximadamente 1 Hz.

Esses dados ainda precisam ser confirmados no Windows com o dispositivo físico.

## Estado desta execução

- Fase 0: implementada a enumeração HID, o payload de 24 bytes e o utilitário `AuraIce.Probe`.
- Fase 1: implementado o provedor inicial de temperatura via `LibreHardwareMonitorLib`.
- Fase 2: implementados o Windows Service, persistência em `ProgramData` e canal local de status.
- Fase 3: criada a primeira interface WPF minimizada na bandeja.
- Fase 4: criado um instalador PowerShell de desenvolvimento; o MSI/WiX continua pendente da validação física.

## Fases

### Fase 0 — prova física

- enumerar o HID;
- abrir a coleção de saída;
- enviar valores controlados de 35,0 °C, 45,0 °C e 85,0 °C;
- registrar comprimento real do relatório e eventuais erros de acesso.

### Fase 1 — telemetria

- selecionar CPU Package, Tctl/Tdie ou sensor equivalente;
- persistir o identificador do sensor escolhido;
- sinalizar ausência, valor inválido ou privilégio insuficiente;
- adicionar fallback somente quando a leitura for confiável.

### Fase 2 — agente persistente

- criar `AuraIce.Service` como `BackgroundService`/Windows Service;
- manter conexão e reconectar quando o USB for removido;
- gravar configurações em `%ProgramData%\AuraIce\config.json`;
- registrar eventos e logs de diagnóstico;
- impedir duas instâncias do agente.

### Fase 3 — interface

- criar `AuraIce.App` em WPF;
- mostrar temperatura, sensor, dispositivo e último erro;
- permitir intervalo, sensor e inicialização com o Windows;
- comunicar-se com o serviço por named pipe local;
- iniciar minimizado na bandeja.

### Fase 4 — distribuição

- publicar `win-x64` autocontido;
- criar MSI com WiX;
- registrar e remover o serviço durante instalação/desinstalação;
- preservar avisos de terceiros;
- validar reinicialização, reconexão e atualização sem internet.

## Critérios de aceite do MVP

- o display mostra a temperatura correta com uma casa decimal;
- a atualização continua por pelo menos 30 minutos;
- o dispositivo é reencontrado após desconectar e reconectar o USB;
- o diagnóstico explica claramente ausência do HID ou ausência do sensor;
- nenhum driver de kernel não assinado é instalado.
