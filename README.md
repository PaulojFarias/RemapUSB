# RemapUSB

App de bandeja para Windows que remapeia os botões de **um controle USB específico** para novas ações. Foi pensado para controles de mídia, como o controle remoto de um PC ligado na TV.

O remapeamento vale só para o controle salvo: o mesmo botão vindo de outro teclado continua funcionando normalmente.

**Exemplos:** o Home do controle abre um app em vez do navegador, o Voltar envia Esc, o Menu vira Play/Pause, outro botão fecha um app.

## Instalar

1. Rode o `RemapUSB-Setup-<versão>.exe` (veja [Gerar o instalador](#gerar-o-instalador)).
2. Na primeira vez, o Windows pode mostrar "O Windows protegeu o computador", porque o instalador não é assinado. Clique em **Mais informações** e depois em **Executar assim mesmo**.

O instalador:
- instala só para o seu usuário, sem pedir administrador;
- cria o atalho no menu Iniciar;
- oferece **iniciar o RemapUSB com o Windows**.

Para atualizar, rode o instalador da versão nova por cima. A configuração continua.

## Usar

1. **Adicionar dispositivo:** clique em Adicionar dispositivo e conecte o controle nos 30 segundos seguintes. Se ele já estiver plugado, tire e coloque de novo. O primeiro dispositivo conectado nesse tempo é o que fica salvo.
2. **Gravar botões:** clique em Gravar botões e aperte cada botão do controle uma vez. A lista começa vazia e cada botão novo vira uma linha. Clique em Concluir gravação.
3. **Escolher a ação:** clique numa linha, escolha a ação e salve. O nome do botão pode ser editado direto na lista.

**Fechar a janela** não fecha o app: ele continua rodando na bandeja, no ícone do controle perto do relógio. Pelo ícone dá para abrir a janela, pausar o remapeamento, ligar ou desligar o "iniciar com o Windows" e sair.

Dá para salvar mais de um controle, cada um com os seus botões.

### Ações disponíveis

| Ação | O que faz |
|---|---|
| Manter original | o botão continua fazendo o que já fazia |
| Não fazer nada | o botão fica sem função |
| Enviar tecla ou atalho | envia a tecla ou combinação capturada (ex.: Esc, Ctrl + Shift + T) |
| Tecla de mídia | Play/Pause, próxima, anterior, parar, volume, mudo |
| Abrir app | abre o app, ou traz a janela para a frente se já estiver aberto |
| Fechar app | pede para o app fechar e, se ele não fechar, encerra à força |
| Abrir/fechar app | alterna: abre se estiver fechado, fecha se estiver aberto |
| Reiniciar app | fecha (se estiver aberto) e abre de novo |
| Abrir site | abre o endereço no navegador padrão |
| Abrir arquivo ou pasta | abre com o programa padrão do Windows |
| Executar comando | roda o comando sem abrir janela |

Os apps podem ser da Store/MSIX ou `.exe` comuns. Os da Store são abertos pelo pacote, então continuam funcionando depois de atualizados.

### Botões de mídia e botões de teclado

Um controle USB aparece no Windows dividido em partes, e cada parte se comporta de um jeito:

| Parte | Botões típicos | Tecla original |
|---|---|---|
| **Mídia** | Home, Voltar, volume, mudo | **bloqueada**: só a nova ação acontece |
| **Teclado** | setas, OK, Menu, Backspace, Delete | o Windows não deixa bloquear só a tecla do controle, então você escolhe |

Para botões de teclado, o editor oferece duas opções:

- **Deixar passar** (padrão): a nova ação acontece e a tecla original também chega ao programa em foco.
- **Neutralizar:** a tecla vira uma tecla sem uso (F13 a F24) em **todos os teclados**, e o app devolve a original aos outros teclados. Pede administrador e reinício, que você aplica em **Configurações → Teclas neutralizadas no Windows**. Com o RemapUSB fechado, a tecla fica sem função em todos os teclados, então só vale a pena para teclas pouco usadas, como o Menu.

O desinstalador desfaz as teclas neutralizadas e oferece reiniciar. Se o pedido de administrador for recusado, ele avisa como desfazer depois.

### Limitações

- **Botão Power:** não é suportado. O Windows o trata antes de qualquer programa.
- **Ponteiro de "air mouse":** é ignorado.
- **Controles Bluetooth:** não são reconhecidos. O app identifica os dispositivos pelo VID/PID USB.
- **Programas abertos como administrador:** o Windows não deixa um programa comum mandar teclas para eles.

## Configuração e log

- **Configuração:** `%AppData%\RemapUSB\config.json`.
- **Log** (pode ser desligado em Configurações): `%LocalAppData%\RemapUSB\logs`. Rodando de dentro do repositório, vai para `app-<máquina>-<data-hora>.txt` na raiz.
- A primeira linha do log traz a versão, o commit e a hora da compilação. A mesma informação aparece em **Configurações → Sobre**.
- Teclas de outros teclados nunca entram no log.

## Desenvolvimento

Requer o .NET 10 SDK. App em WPF (tema Fluent do Windows), sem pacotes externos.

```bash
dotnet run --project src/RemapUSB.App
```

### Gerar o instalador

Com o [Inno Setup 6](https://jrsoftware.org/isinfo.php) instalado, dê dois cliques em **`installer\gerar-instalador.cmd`**. Ele publica o app, compila o instalador e abre a pasta `installer\Output` com o `RemapUSB-Setup-<versão>.exe`.

Os dois passos que ele faz, se precisar rodar à mão:

1. **Publicar:** `dotnet publish src/RemapUSB.App -p:PublishProfile=win-x64`. Sai um `RemapUSB.exe` único em `installer\publish`, que roda sem o .NET instalado.
2. **Compilar:** abrir `installer\RemapUSB.iss` no Inno Setup → Build → Compile.

**Versão:** `<base>.<quantidade de commits>`, por exemplo `1.0.27`. O último número sobe sozinho a cada commit. A base (`RemapUsbBaseVersion` no `RemapUSB.App.csproj`) é manual: mude para marcar uma versão maior.

**Desinstalação:** o desinstalador roda `RemapUSB.exe --desfazer-teclas`, que remove do Scancode Map só as teclas que o app neutralizou:

| Código de saída | O desinstalador |
|---|---|
| 0, nada a desfazer | desinstala sem pedir administrador |
| 10, desfeito | oferece reiniciar no final (a tecla só volta ao normal depois do reinício) |
| 1, administrador recusado ou erro | avisa que as teclas continuam trocadas e como desfazer |

### Como o motor funciona

O hook de teclado e o Raw Input rodam numa thread própria, de prioridade alta, separada da interface.

- **Botões de mídia:** o hook segura a tecla que o Windows gera para o botão e cruza com o Raw Input, que diz de qual dispositivo ela veio. A tecla que o Windows gera para cada botão é aprendida na gravação.
- **Botões de teclado:** bloquear no hook não funciona, porque com a tecla bloqueada o Windows nem gera o Raw Input. Por isso existem o "deixar passar" e o "neutralizar" (Scancode Map). O app preserva entradas do Scancode Map que não são dele.
- **Janela do RemapUSB em foco:** nesse caso o Windows manda o botão de mídia como comando de app (`WM_APPCOMMAND`), em vez de passar a tecla pelo hook. A janela descarta o comando de botões remapeados e a ação dispara assim mesmo.

Linhas `[HOOK]` e `[APPCOMMAND]` no log:

| Linha | Significa |
|---|---|
| `chegou ao hook com N ms de atraso` | o Windows demorou mais de 40 ms para chamar o hook |
| `segurada antes do Raw` | a tecla chegou ao hook antes do Raw do controle; o app espera até 60 ms |
| `o Raw chegou, mas o hook não recebeu a tecla` | o Windows não passou a tecla pelo hook: a ação dispara assim mesmo, e a linha diz qual janela estava em foco |
| `[APPCOMMAND] janela do RemapUSB recebeu ...` | com a janela do app em foco, o botão chegou como comando de app; se o botão está remapeado, o comando é descartado |

### Estrutura do repositório

| Pasta | Conteúdo |
|---|---|
| `src/RemapUSB.App` | o app |
| `installer` | script do Inno Setup e `gerar-instalador.cmd` |
| `src/RemapUSB.Probe` | protótipo 1: console que mostra cada botão e por qual parte do controle ele chega |
| `src/RemapUSB.Proto2a` | protótipo 2a: remapeamento com mapeamentos fixos, que provou a técnica do app |
| `tools` | `.reg` que o protótipo 2a usava para neutralizar o Menu (o app faz isso em Configurações) |
| `docs/mockup.html` | mockup clicável das telas, aprovado antes do app |

Os protótipos ficaram como registro de como a técnica foi validada. **Não rode o Proto2a junto com o app**, porque os dois remapeiam os mesmos botões.
