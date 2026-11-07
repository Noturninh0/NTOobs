# NTOobs

**Controle o OBS Studio com as teclas do seu teclado durante a live.**

O NTOobs funciona como um StreamDeck feito de teclado. Você escolhe uma tecla, diz o que ela deve fazer no OBS (trocar de cena, mutar o microfone, iniciar a gravação...) e pronto: com o programa aberto, a tecla funciona mesmo que você esteja dentro de um jogo ou de outra janela.

Você pode usar o teclado principal ou **um segundo teclado (ou macropad) só para o OBS**. O NTOobs sabe de qual teclado cada tecla veio, então o teclado extra não atrapalha o que você digita no principal.

## Baixar

Vá em **[Releases](../../releases/latest)** e baixe:

| Arquivo | Para quê |
|---|---|
| `NTOobs-Setup.exe` | Instalador (recomendado). Pergunta se você quer um atalho na Área de Trabalho. |
| `NTOobs.exe` | Versão portátil. Basta dar dois cliques, sem instalar. |

Requisitos: Windows 10 ou 11 (64 bits) e OBS Studio 28 ou mais novo. Não precisa instalar o .NET.

> O Windows pode mostrar o aviso "O Windows protegeu o computador" porque o programa não é assinado digitalmente. Clique em **Mais informações** e depois em **Executar assim mesmo**.

## Instalação

1. Abra o `NTOobs-Setup.exe`.
2. Escolha a pasta (ou deixe a padrão).
3. Na tela **Atalhos**, deixe marcada a opção **"Deseja criar um atalho na Área de Trabalho?"** se quiser o ícone na Área de Trabalho. Desmarque se não quiser.
4. Clique em **Instalar** e depois em **Concluir**. Marque "Abrir o NTOobs agora" para já começar.

## Preparar o OBS (só uma vez)

1. No OBS, abra **Ferramentas → Configurações do servidor WebSocket**.
2. Marque **Ativar servidor WebSocket**.
3. Deixe a porta **4455** (padrão).
4. Se quiser senha, marque **Ativar autenticação** e defina uma. Anote a senha.
5. Clique em **OK**.

## Usar o NTOobs

### 1. Conectar ao OBS
Deixe o OBS aberto. No NTOobs, abra a aba **Conexão** (ícone de globo), confira servidor `localhost` e porta `4455`, digite a senha (se tiver) e clique em **Conectar**. O indicador no topo fica verde com "Conectado". Marque **Conectar ao abrir o app** para não precisar repetir; se o OBS fechar e abrir de novo, o NTOobs reconecta sozinho.

### 2. Criar um atalho
Na aba **Atalhos** (ícone de teclado), no cartão **Novo atalho**:

1. Clique em **Capturar tecla** e aperte a tecla desejada, no teclado que você quiser.
2. Se for um teclado externo e você quiser que o atalho valha **só nele**, marque **Somente neste teclado**.
3. Escolha a **ação**:
   - Trocar de cena
   - Mutar / desmutar fonte de áudio
   - Iniciar / parar gravação
   - Iniciar / parar transmissão
   - **Salvar Backtrack** (clipe dos últimos segundos, do plugin de backtrack/vertical)
   - **Executar um atalho do OBS (lista)**: qualquer atalho que o OBS conhece, inclusive os de plugins
   - **Simular combinação de teclas no OBS**: aperta uma combinação como `Ctrl+Alt+F9` dentro do OBS
4. Para cena ou áudio, informe o **destino**: clique em um nome da lista de cenas do OBS (use **Atualizar** se criou cenas novas) ou digite o nome exato.
5. Clique em **Adicionar atalho**.

### Backtrack e plugins do OBS

O NTOobs lê a lista de atalhos direto do OBS conectado, então funciona com um OBS modificado, com plugins extras.

- **Salvar Backtrack:** vem preenchido com `Ctrl+Alt+Shift+6`. No OBS, abra **Configurações → Atalhos**, procure o atalho "Salvar Backtrack" e coloque essa mesma combinação nele. Se você usar outra combinação no OBS, troque o campo de destino no NTOobs. O NTOobs "aperta" essa combinação dentro do OBS quando você usa a tecla que escolheu.
- **Executar um atalho do OBS (lista):** escolha a ação, clique em **Atualizar** (com o OBS conectado) e use o campo de filtro para achar o atalho, por exemplo digitando `backtrack`. Clique no nome para usar. Os nomes da lista são os nomes internos do OBS, que podem ser diferentes do texto mostrado em Configurações → Atalhos.
- Alguns atalhos de plugins pertencem a uma fonte ou saída específica e podem não disparar pelo nome. Nesse caso, defina uma combinação para ele em Configurações → Atalhos do OBS e use **Simular combinação de teclas no OBS**.

### 3. Testar
Na lista de atalhos, o botão **Testar** dispara a ação sem apertar a tecla. O **Painel** mostra quantas vezes cada atalho foi usado nesta sessão. A aba **Registro** mostra tudo o que aconteceu, incluindo erros.

### Outros detalhes
- O botão de sol/lua no topo alterna entre tema escuro e claro.
- Os atalhos ficam salvos em `%AppData%\NTOobs\config.json`.

## Dicas

- Use as teclas **F13 a F24** ou um teclado/macropad extra para os atalhos. O NTOobs apenas observa as teclas, então uma tecla comum (como `A`) também continua digitando no programa em foco.
- Enquanto você digita dentro das caixas de texto do próprio NTOobs, os atalhos ficam pausados.
- O nome da cena ou da fonte precisa ser igual ao do OBS, com maiúsculas e acentos.

## Problemas comuns

| Sintoma | O que fazer |
|---|---|
| Não conecta | Confirme que o OBS está aberto, o servidor WebSocket ativado, a porta correta (4455) e a senha igual à do OBS. A aba **Registro** mostra o motivo. |
| A tecla não faz nada | Verifique se o indicador está verde e se não há outro atalho igual. Teste com o botão **Testar**. |
| Trocar de cena dá erro | O nome da cena não bate com o do OBS. Use a lista de cenas para escolher. |
| Mutar não funciona | Use o nome exato da fonte de áudio como aparece no Mixer de áudio do OBS (por exemplo `Mic/Aux`). |

## Limitações

- A ação de simular combinação aceita letras, números, F1 a F24, teclas do teclado numérico (`Num0` a `Num9`) e teclas como `Space`, `Enter`, `Esc`, `Tab`, setas, `Home`, `End`, `PageUp` e `PageDown`, com `Ctrl`, `Alt`, `Shift` e `Win`.
- Dois teclados do mesmo modelo (mesmo fabricante e produto) não são diferenciados entre si.
- A senha do OBS fica salva em texto simples no `config.json`.
- O programa não é assinado digitalmente (veja o aviso do Windows acima).

## Para desenvolvedores

Projeto em C# / WPF (.NET 8), sem dependências externas. O cliente do OBS fala o protocolo obs-websocket v5 diretamente, e a leitura de teclas por teclado usa a API Raw Input do Windows.

```
build.bat                       compila e abre o NTOobs.exe (precisa do .NET 8 SDK, instala pelo winget)
NTOobs/                         código-fonte
installer/NTOobs.iss            script do instalador (Inno Setup 6)
.github/workflows/release.yml   compila e publica a Release
```

### Publicar uma nova versão

O GitHub Actions compila o `.exe`, gera o instalador e anexa tudo à Release quando você cria uma tag:

```
git tag v1.0.0
git push origin v1.0.0
```

Para mudar o número de versão mostrado no instalador, edite `AppVersion` em `installer/NTOobs.iss`.
