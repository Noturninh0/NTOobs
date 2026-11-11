# Política de privacidade do NTOobs

Última atualização: 11 de novembro de 2026

O NTOobs é um aplicativo para Windows que controla o OBS Studio durante lives. Esta página explica o que acontece com os seus dados.

## O que o NTOobs guarda

Tudo fica **só no seu computador**, na pasta `%AppData%\NTOobs` (ou na pasta do aplicativo quando instalado pela Microsoft Store):

- as configurações do app (conexão com o OBS, atalhos, perfis, macros, contadores, temas);
- a senha do OBS WebSocket e, se você entrar com a Twitch, o token de acesso da Twitch;
- os sons, imagens e vídeos que você importa (soundboard, alertas e temas);
- os arquivos de "Marcar momento", na pasta Documentos\NTOobs\Marcadores.

O NTOobs **não tem servidor próprio**, não cria conta, não coleta estatísticas de uso e não envia nenhum dado para o desenvolvedor.

## Conexões que o NTOobs faz

O app só se conecta a serviços que você mesmo configurou:

- **OBS Studio**, no seu computador ou na sua rede (obs-websocket);
- **Twitch** (chat e alertas), se você informar o seu canal ou entrar com a Twitch. A comunicação é direta entre o seu PC e a Twitch;
- **YouTube ou Spotify**, quando você usa a Música da live (o player roda dentro do OBS ou no aplicativo do Spotify);
- **Controle pelo celular**, se você ativar: um servidor local na sua rede Wi-Fi, protegido por um código.

Cada um desses serviços tem a sua própria política de privacidade.

## Como apagar os seus dados

Desinstale o NTOobs e apague a pasta `%AppData%\NTOobs`. Para sair da Twitch, use o botão Sair na aba Twitch ou remova o acesso em twitch.tv → Configurações → Conexões.

## Contato

Dúvidas: abra uma issue em https://github.com/Noturninh0/NTOobs/issues
