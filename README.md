# RemapUSB

App de bandeja para Windows que remapeia os botões de **um dispositivo USB específico** (pensado para controles de mídia) para novas ações: outra tecla, tecla de mídia, abrir/fechar app, abrir site, executar comando.

O remapeamento vale só para o dispositivo salvo. O mesmo botão vindo de outro teclado continua normal.

## Como vai funcionar

1. **Escutar:** por 30s, o primeiro dispositivo USB conectado é salvo (VID/PID e as partes que ele expõe).
2. **Ao iniciar:** se existe dispositivo salvo, o app fica na bandeja escutando; se não, oferece a escuta.
3. **Gravar botões:** a lista começa vazia e cada botão apertado no dispositivo vira uma linha.
4. **Remapear:** cada linha recebe uma ação. O padrão é "Manter original".

## Como o Windows vê um controle de mídia

Um controle USB se divide em partes, e cada parte segue um caminho diferente no Windows:

| Parte | Botões típicos | Como o app captura |
|---|---|---|
| Teclado | setas, OK, voltar | Raw Input + hook de teclado |
| Consumer control | play, volume, mute | Raw Input + hook de teclado |
| Controlador de sistema | **Power**, Sleep | Não aparece no Raw Input; exige o app assumir a interface USB |

## Estado

- [x] **Protótipo 1** (`src/RemapUSB.Probe`): console que mostra cada botão e por qual parte ele chegou.
- [ ] **Protótipo 2a** (`src/RemapUSB.Proto2a`): bloquear botões do controle e disparar outra ação, sem trocar driver.
- [ ] **Protótipo 2b:** assumir a interface do Power e ler o botão sem o PC desligar.
- [ ] App de bandeja (WPF), gravação, ações e configuração em JSON.

## Rodar o Protótipo 1

Requer .NET 10 SDK. Com o controle plugado:

```bash
dotnet run --project src/RemapUSB.Probe -- VID_0627
```

O argumento filtra pelo trecho do caminho do dispositivo (troque pelo VID do seu controle). Sem argumento, mostra todos.

O probe escuta com **qualquer janela em foco**, então dá para apertar os botões mesmo que eles abram o navegador. Tudo é gravado também em `probe-<máquina>-<data-hora>.txt` na raiz do repositório.

Linhas `[RAW]` dizem de qual parte do dispositivo veio o evento. Linhas `[HOOK]` mostram o que passou pelo fluxo de teclado do Windows (sem saber o dispositivo).

Com filtro, o hook só grava teclas que chegam até 150ms antes ou depois de um evento do dispositivo filtrado, para não registrar o que você digita em outros teclados. **Sem filtro, o hook grava todas as teclas**: não rode sem filtro enquanto digita algo sensível.

> ⚠️ Não aperte o Power durante o teste: o Windows ainda trata esse botão e desliga o PC.

## Rodar o Protótipo 2a

Mapeamentos fixos no código (`Program.cs`), para o controle LE-7655 (VID_0627 / PID_697D):

| Botão | Ação |
|---|---|
| Voltar | Esc |
| Home | fecha o TubeTV (se aberto) e abre de novo |
| Menu de contexto | Play/Pause |

Os demais botões continuam normais.

**Antes, uma vez:** dê dois cliques em `tools/menu-para-f24.reg`, confirme e **reinicie o Windows**. Para desfazer, use `tools/desfazer-menu-para-f24.reg` e reinicie. O `.reg` substitui qualquer Scancode Map que já exista na máquina.

```bash
dotnet run --project src/RemapUSB.Proto2a
```

O log vai para `proto2a-<máquina>-<data-hora>.txt` na raiz do repositório. A primeira linha depois do TubeTV diz se o Scancode Map está configurado. Linhas `[DONGLE]` dizem se o controle estava plugado ao iniciar e quando ele foi desconectado ou reconectado.

**Botões de mídia (Voltar, Home):** o hook de teclado segura a tecla e cruza com o Raw Input para saber se veio do controle. O Raw chega antes do hook, então a decisão é imediata. Se em 60ms nenhum Raw do controle aparecer, a tecla veio de outro teclado e é reenviada ao Windows.

**Botões de teclado (Menu):** bloquear no hook não funciona, porque com a tecla bloqueada o Windows nem gera o Raw e não dá para saber de onde ela veio. Por isso a tecla é **neutralizada**: o Scancode Map faz o Menu de todos os teclados virar F24, que nenhum programa usa. O F24 do controle passa sem efeito e dispara a ação; o F24 de outro teclado é devolvido como Menu. Com o app fechado, a tecla Menu de qualquer teclado fica sem função.
