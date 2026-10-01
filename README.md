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
- [ ] **Protótipo 2:** assumir a interface do Power e ler o botão sem o PC desligar.
- [ ] App de bandeja (WPF), gravação, ações e configuração em JSON.

## Rodar o Protótipo 1

Requer .NET 10 SDK. Com o controle plugado:

```bash
dotnet run --project src/RemapUSB.Probe -- VID_0627
```

O argumento filtra pelo trecho do caminho do dispositivo (troque pelo VID do seu controle). Sem argumento, mostra todos.

Linhas `[RAW]` dizem de qual parte do dispositivo veio o evento. Linhas `[HOOK]` mostram o que passou pelo fluxo de teclado do Windows (sem saber o dispositivo).

> ⚠️ Não aperte o Power durante o teste: o Windows ainda trata esse botão e desliga o PC.
