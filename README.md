# NTOobs

**Controle o OBS Studio com as teclas do seu teclado durante a live.**

<a href="https://apps.microsoft.com/detail/9PG141XJTKRD?mode=direct"><img src="https://get.microsoft.com/images/pt-br%20dark.svg" width="200" alt="Baixar na Microsoft Store"/></a>

O NTOobs funciona como um StreamDeck feito de teclado. Você escolhe uma tecla, diz o que ela deve fazer no OBS (trocar de cena, mutar o microfone, iniciar a gravação...) e pronto: com o programa aberto, a tecla funciona mesmo que você esteja dentro de um jogo ou de outra janela.

Você pode usar o teclado principal ou **um segundo teclado (ou macropad) só para o OBS**. O NTOobs sabe de qual teclado cada tecla veio, então o teclado extra não atrapalha o que você digita no principal.

## Baixar

### Microsoft Store (recomendado)

O NTOobs está na **[Microsoft Store](https://apps.microsoft.com/detail/9PG141XJTKRD)**. Instalando por lá, o Windows não mostra aviso de segurança e o app se atualiza sozinho. Também dá para abrir a Microsoft Store no PC e pesquisar **NTOobs**.

### GitHub

Se preferir, vá em **[Releases](../../releases/latest)** e baixe:

| Arquivo | Para quê |
|---|---|
| `NTOobs-Setup.exe` | Instalador (recomendado). Pergunta se você quer um atalho na Área de Trabalho. |
| `NTOobs.exe` | Versão portátil. Basta dar dois cliques, sem instalar. |

Requisitos: Windows 10 ou 11 (64 bits) e OBS Studio 28 ou mais novo. Não precisa instalar o .NET.

> Nas versões do GitHub, o Windows pode mostrar o aviso "O Windows protegeu o computador" porque o programa não é assinado digitalmente (a versão da Microsoft Store não tem esse aviso). Clique em **Mais informações** e depois em **Executar assim mesmo**.

## Instalação (versão do GitHub)

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
   - **Alternar cena**: vai para a cena (ex.: Volto já) e, apertando de novo, volta para a anterior
   - **Voltar para a cena anterior**
   - **Começar live**, **Marcar momento** e **Replay instantâneo** (veja Ferramentas)
   - **Tocar som do soundboard**, **Executar macro** e **Contador** (somar, diminuir, zerar)
   - **Pausar / retomar a música live**
4. Informe o **destino** quando a ação pedir: clique em um nome da lista ao lado (cenas, fontes de áudio, atalhos do OBS, macros, sons ou contadores; use **Atualizar** se criou algo novo no OBS) ou digite o nome exato.
5. Clique em **Adicionar atalho**.

### Backtrack e plugins do OBS

O NTOobs lê a lista de atalhos direto do OBS conectado, então funciona com um OBS modificado, com plugins extras.

- **Salvar Backtrack:** vem preenchido com `Ctrl+Alt+Shift+6`. No OBS, abra **Configurações → Atalhos**, procure o atalho "Salvar Backtrack" e coloque essa mesma combinação nele. Se você usar outra combinação no OBS, clique em **Capturar combinação** e aperte as teclas que você configurou lá (ou digite no campo). O NTOobs "aperta" essa combinação dentro do OBS quando você usa a tecla que escolheu.
- **Executar um atalho do OBS (lista):** escolha a ação, clique em **Atualizar** (com o OBS conectado) e use o campo de filtro para achar o atalho, por exemplo digitando `backtrack`. Clique no nome para usar. Os nomes da lista são os nomes internos do OBS, que podem ser diferentes do texto mostrado em Configurações → Atalhos.
- Alguns atalhos de plugins pertencem a uma fonte ou saída específica e podem não disparar pelo nome. Nesse caso, defina uma combinação para ele em Configurações → Atalhos do OBS e use **Simular combinação de teclas no OBS**.

### 3. Testar
Na lista de atalhos, o botão **Testar** dispara a ação sem apertar a tecla. O **Painel** mostra quantas vezes cada atalho foi usado nesta sessão. A aba **Registro** mostra tudo o que aconteceu, incluindo erros.

### Outros detalhes
- O botão de sol/lua no topo alterna entre tema escuro e claro.
- Os atalhos ficam salvos em `%AppData%\NTOobs\config.json`.

## Música da live

A aba **Música** (ícone de nota musical) toca uma playlist durante a live, dentro do OBS.

1. Conecte ao OBS (aba Conexão).
2. Na aba Música, cole o link de uma playlist do **YouTube** ou do **Spotify**.
3. No YouTube, ajuste o volume e marque se quer repetir a playlist ou tocar em ordem aleatória.
4. Clique em **Iniciar / aplicar**. No YouTube, o NTOobs cria no OBS uma fonte chamada **NTOobs Música**, com o áudio no Mixer de áudio, e a coloca em todas as cenas (fora da tela, só o som importa).
5. Para pausar e retomar com uma tecla: na própria aba Música, clique em **Definir tecla** e aperte a tecla que quiser, em qualquer teclado. Marque "Somente no teclado em que eu apertar" se for um teclado externo. A tecla também aparece na aba **Atalhos**, onde você pode criar outras com a ação **Pausar / retomar a música live**.

Como a música funciona:
- Trocar de cena não interrompe a música, porque a fonte está em todas as cenas. Ela só pausa pelo atalho (ou pelo botão da aba Música).
- Se você fechar o NTOobs, a música do YouTube continua tocando no OBS. Para pausar ou retomar, é preciso abrir o NTOobs de novo.
- A playlist, o volume e as opções ficam salvos. Ao abrir o NTOobs e conectar ao OBS, ele confere a fonte e, se o player não estiver respondendo (por exemplo, se o OBS foi aberto com o NTOobs fechado), recarrega a fonte.
- Se mudar o link e clicar em **Iniciar / aplicar**, a nova playlist passa a tocar.
- **Parar e remover do OBS** apaga a fonte de música do OBS (e pausa o Spotify, se estiver tocando).

### Spotify (músicas inteiras)

Com link do Spotify, o NTOobs usa o **aplicativo Spotify do computador**, e não um player dentro do OBS (esse player só tocava trechos de 30 segundos).

- É preciso ter o Spotify para Windows instalado e com a sua conta conectada.
- Ao clicar em **Iniciar / aplicar**, o NTOobs abre a playlist no Spotify e cria no OBS a fonte **NTOobs Spotify** (Captura de áudio de aplicativo). Ela captura só o som do Spotify e fica em todas as cenas.
- Se a playlist abrir sem começar a tocar, clique no play verde dela uma vez. Depois disso, o seu atalho pausa e retoma só o Spotify.
- Repetir, ordem aleatória e volume são definidos no próprio Spotify. O volume que vai para a live também pode ser ajustado no mixer do OBS.
- O atalho funciona com o NTOobs aberto (pode ficar minimizado). Se fechar o NTOobs, o Spotify continua tocando normalmente.
- Requer OBS 28 ou mais novo (Captura de áudio de aplicativo, Windows 10 2004 ou mais novo).

Cuidado com direitos autorais: músicas protegidas podem silenciar ou derrubar suas transmissões e VODs.

## Perfis por jogo

Na aba **Atalhos**, o cartão **Perfis** guarda um conjunto de teclas para cada jogo.

- Digite um nome e clique em **Novo perfil**. Clique em um perfil da lista para ativá-lo: as teclas da aba Atalhos são sempre as do perfil ativo.
- Para a troca automática: abra o jogo, volte ao NTOobs, clique em **Escolher programa aberto** e escolha o jogo. Com **Trocar de perfil sozinho** marcado, quando o jogo ficar na frente, o NTOobs ativa o perfil dele.
- A tecla de pausar a música é copiada para cada perfil novo.

## Macros

Na aba **Macros**, uma tecla faz várias ações em sequência, com espera entre elas se quiser.

- Crie a macro, escolha uma ação na lista, o destino e clique em **Adicionar passo**. Use as setas para mudar a ordem.
- **Criar exemplo Volto já** monta uma macro pronta: Alternar cena (Volto já) + mutar o microfone + pausar a música. Apertando de novo, tudo volta. Confira os nomes da cena e do microfone.
- Clique em **Definir tecla** para escolher a tecla da macro (no perfil ativo).

## Soundboard

Na aba **Soundboard**, clique em **Adicionar sons...** e escolha arquivos mp3, wav, ogg ou m4a (eles são copiados para a pasta do NTOobs). Em cada som: **Tocar**, **Tecla** e o volume.

O som sai na live pela fonte **NTOobs Soundboard**, criada sozinha em todas as cenas. Para ouvir no seu fone também, marque o monitoramento. Se o som sair duplicado na live, desmarque (o Áudio do Desktop pode estar capturando o fone).

## Ferramentas

Na aba **Ferramentas** (ícone de chave):

- **Estilo dos textos na live**: escolha o tipo de letra (as fontes instaladas no PC, com busca), o tamanho, a cor (cores prontas ou código como `#F5A524`), negrito e contorno, com prévia. Vale para os contadores e para a contagem do Começar live (que fica 1,5x maior). Os alertas da Twitch usam o mesmo tipo de letra. **Aplicar nos textos do OBS** atualiza os textos que já existem.
- **Contadores na tela**: crie contadores (mortes, vitórias...) e use as ações de contador na aba Atalhos. **Mostrar na live** cria um texto no OBS que atualiza sozinho. No formato, `{nome}` vira o nome e `{n}` o número.
- **Marcar momento**: salva o horário da live e da gravação num arquivo do dia em `Documentos\NTOobs\Marcadores`, para achar os melhores momentos e cortar depois.
- **Replay instantâneo**: salva o Replay Buffer do OBS, mostra o vídeo em tela cheia numa cena só dele e volta para a cena anterior. Ative antes no OBS: Configurações, Saída, Replay Buffer.
- **Começar live**: confere se o microfone está mutado e se as cenas existem, inicia a transmissão (e a gravação, se marcado), mostra "Começando em 5:00" na cena de início e troca para a cena principal no fim. **Pular contagem** vai direto.
- **Saúde da live**: o Painel mostra tempo no ar, bitrate, quadros perdidos, CPU e FPS. Se a internet ou o PC começarem a falhar, aparece um aviso (com som, se marcado).

## Twitch

Na aba **Twitch**:

- Só com o **nome do canal**, o NTOobs lê o chat e recebe os alertas de **sub, raid e bits**.
- **Ligar / desligar alertas**: no topo do card Alertas na tela (ou no Painel) há um botão para desligar todos os alertas, e dá para definir uma tecla para isso. Desligado, nenhum alerta aparece na live (ex.: no meio de uma partida). A ação **Ligar / desligar alertas na tela** também existe na aba Atalhos, nas macros e no celular.
- **Alertas na tela**: escolha um dos 6 modelos prontos (Clássico, Neon, Minimalista, Faixa, Pop e Conquista), a posição (topo, centro ou embaixo) e o tempo na tela.
- Em cada tipo de alerta (follow, sub, raid, bits) dá para colocar **a sua própria imagem, GIF ou vídeo** (png, jpg, gif, webp, mp4, webm, até 50 MB) e **o seu próprio som**. Sem som próprio, toca o som padrão. Vídeos tocam até o fim (no máximo 15 segundos).
- **Ver prévia no navegador** mostra os alertas passando em sequência. **Adicionar alertas no OBS** cria a fonte **NTOobs Alertas** em todas as cenas. Os alertas só aparecem com o NTOobs aberto.
- **Comandos do chat**: `!musica` responde a música que está tocando, `!marcar` (você e moderadores) marca o momento, e você pode criar comandos seus, como `!discord`. Use `{user}` para chamar a pessoa pelo nome.
- Para **responder no chat** e mostrar **follows**, entre com a Twitch (uma vez só):
  1. Clique em **Abrir o console da Twitch** e registre um aplicativo.
  2. Em URLs de redirecionamento OAuth, cole o endereço mostrado no NTOobs (botão **Copiar**).
  3. Categoria: Chat Bot. Tipo de cliente: Público.
  4. Copie o ID do cliente, cole no NTOobs e clique em **Entrar com a Twitch**.

## Controle pelo celular

Na aba **Celular**, marque **Ativar controle pelo celular** e abra o endereço mostrado no navegador do celular (no mesmo Wi-Fi). Aparecem botões grandes com os atalhos do perfil ativo, macros, sons, contadores e os controles da live.

- Na primeira vez, o Windows pergunta se o NTOobs pode usar a rede: marque **Redes privadas** e clique em **Permitir**.
- O código no endereço impede que outras pessoas da rede controlem a sua live. Se alguém descobrir, clique em **Gerar novo código**.
- Dica: no navegador do celular, use **Adicionar à tela inicial** para abrir como um app.

## Aparência

Na aba **Aparência** (ícone de pincel):

- **Tema**: escuro ou claro (o padrão). O botão no topo da janela também alterna.
- **Meus temas**: clique em **Importar tema...** e escolha uma ou várias imagens do seu PC. Cada imagem vira um tema de fundo. Clique num tema para usar, ajuste a **visibilidade da imagem** e use **Sem imagem** para voltar ao escuro/claro puro. As imagens são copiadas para a pasta do NTOobs e ficam só no seu computador.
- **Tamanho recomendado**: a aba mostra o tamanho ideal da imagem (por padrão **1920 × 1292 px**, a proporção da janela; o número acompanha se você mudar o tamanho da janela) e avisa se a imagem escolhida vai ser cortada ou ficar borrada.
- **Ajuste da imagem**: **Preencher toda a janela** (padrão) faz qualquer imagem cobrir o app inteiro, cortando o que sobra. Em imagens em pé, o topo é mantido. **Mostrar a imagem inteira** não corta nada.

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
- A senha do OBS e o login da Twitch ficam salvos em texto simples no `config.json`.
- O programa não é assinado digitalmente (veja o aviso do Windows acima).

## Para desenvolvedores

Projeto em C# / WPF (.NET 8), sem dependências externas. O cliente do OBS fala o protocolo obs-websocket v5 diretamente, e a leitura de teclas por teclado usa a API Raw Input do Windows.

```
build.bat                       compila e abre o NTOobs.exe (precisa do .NET 8 SDK, instala pelo winget)
NTOobs/                         código-fonte
installer/NTOobs.iss            script do instalador (Inno Setup 6)
msix/                           manifesto e ícones do pacote da Microsoft Store
PRIVACIDADE.md                  política de privacidade (exigida pela Store)
.github/workflows/release.yml   compila e publica a Release
```

### Publicar uma nova versão

O GitHub Actions compila o `.exe`, gera o instalador e anexa tudo à Release quando você cria uma tag:

```
git tag v1.0.0
git push origin v1.0.0
```

Para mudar o número de versão mostrado no instalador, edite `AppVersion` em `installer/NTOobs.iss`.

O mesmo workflow também gera `NTOobs.msix` (no artefato da execução, não na Release). Para atualizar a Microsoft Store, crie um novo envio no Partner Center e envie esse arquivo; a versão do pacote vem do `<Version>` do `NTOobs.csproj`, que precisa ser maior que a da Store.
