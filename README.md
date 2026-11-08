# SA Skin Manager

Launcher e gerenciador para trocar as skins de personagens do **GTA San Andreas (PC, versão clássica)**.
Mostra todos os NPCs do jogo, deixa escolher "trocar este personagem por esta skin" e instala sozinho,
com desfazer em um clique.

## Como funciona

- **Primeira execução:** o programa tenta achar a pasta do jogo (Steam, Rockstar, caminhos comuns). Você confirma ou escolhe a pasta.
  Com a opção *"Carregar todos os modelos de NPC agora"* marcada, ele lê o `data/peds.ide` e o `models/gta3.img`
  e monta a lista completa de personagens com miniaturas. Depois disso tudo fica em cache.
- **Biblioteca de skins:** importe arquivos `.dff` + `.txd`, uma pasta ou um `.zip` (ou arraste para a lista).
- **Trocar:** selecione o personagem à esquerda, a skin à direita e clique em *Trocar por esta skin*.
  O programa renomeia os arquivos para o nome do modelo alvo (ex.: `ballas1.dff` / `ballas1.txd`).
- **Modos de instalação:**
  - **Mod Loader** (recomendado, usado automaticamente se o `modloader.asi` existir): grava em
    `modloader\SaSkinManager\<modelo>\`. Os `.img` originais não são alterados.
  - **IMG direto:** substitui as entradas dentro do `gta3.img`. Antes da primeira alteração ele faz um backup completo
    (`gta3.img.skinmanager.bak`) e guarda o original de cada arquivo trocado para o *Desfazer*.
- **Trocas ativas:** lista de tudo que foi trocado, com *Desfazer* individual e *Restaurar tudo*.
- **Jogar:** abre o `gta_sa.exe`.

### CJ

O CJ não é um personagem comum: o corpo dele é montado com peças de roupa do `models/player.img`.
Importe pacotes de "CJ replacement" pelo botão **+ Pacote do CJ**: os arquivos mantêm os nomes originais e vão para
`modloader\SaSkinManager\cj\player.img\` ou para dentro do `player.img`.
Para jogar com um NPC comum no lugar do CJ, use um mod como o Skin Selector (CLEO).

## Instalar

Baixe o `SaSkinManager.exe` na página de [Releases](../../releases) e execute. Não precisa instalar o .NET.

> Recomendado: no primeiro teste use uma **cópia** da pasta do jogo.

## Requisitos

- Windows 10/11
- GTA San Andreas PC clássico (1.0, Steam ou "downgrade"). A *Definitive Edition* usa outro formato e não é suportada.
- Opcional: [Mod Loader](https://github.com/thelink2012/modloader)

Dados do programa ficam em `%AppData%\SaSkinManager` (configurações, biblioteca, cache, backups por entrada).

## Compilar

```bash
dotnet build
dotnet test
dotnet publish src/SaSkinManager.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## Estrutura

- `src/SaSkinManager.Core`: formatos (IMG VER2, IDE, TXD/DXT), catálogo de personagens, biblioteca e lógica de troca. Sem WPF, testável.
- `src/SaSkinManager.App`: interface WPF (.NET 8).
- `tests/SaSkinManager.Core.Tests`: testes xUnit com IMG/TXD sintéticos.

## Roadmap

- v2: preview 3D do modelo (`.dff` + `.txd`), lado a lado original × nova; perfis de trocas.
- v3: troca de veículos (carros, motos, barcos, aviões, helicópteros…) via `data/vehicles.ide`.

## Licença

[GPL-3.0](LICENSE). Nenhum arquivo do jogo (Rockstar Games) é distribuído com este projeto.
