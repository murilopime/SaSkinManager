<p align="center"><img src="docs/icon.png" width="96" alt="ícone"></p>

# SA Skin Manager

[English](README.md) · **Português**

Launcher e gerenciador para trocar as skins de personagens do **GTA San Andreas (PC, versão clássica)**.
Mostra todos os personagens do jogo, deixa escolher "trocar este personagem por esta skin" e instala sozinho,
com desfazer em um clique.

## Como funciona

- **Primeira execução:** o programa tenta achar a pasta do jogo (Steam, Rockstar, caminhos comuns). Você confirma ou escolhe a pasta.
  Com a opção *"Carregar todos os modelos de NPC agora"* marcada, ele lê o `data/peds.ide`, o `models/gta3.img` e o
  `models/cutscene.img` e monta a lista completa de personagens com miniaturas. Depois disso tudo fica em cache.
- **Só personagens:** o programa reconhece personagens pelo esqueleto (32 ossos ou mais). Objetos e carros animados
  das cutscenes ficam de fora.
- **Biblioteca de skins:** importe arquivos `.dff` + `.txd`, uma pasta ou um `.zip` (ou arraste para a lista).
  Dá para buscar, ordenar (recentes, nome, mais usadas), renomear (`F2`, clique duplo ou botão direito) e ver em quantos
  personagens cada skin está em uso. O menu **⋯** exporta a biblioteca inteira num `.zip` de backup e restaura depois.
- **Prévia 3D:** o personagem original e a nova skin aparecem lado a lado em 3D, girando juntos.
  Arraste para girar, use a roda do mouse para o zoom e clique duas vezes para voltar. A grade de personagens usa
  miniaturas 3D e os personagens na tela ficam girando (ou só o que está sob o mouse, nas configurações).
- **Trocar:** selecione o personagem à esquerda, a skin à direita e clique em *Trocar por esta skin*.
  Com `Ctrl`/`Shift` dá para selecionar vários personagens e aplicar a mesma skin em todos.
- **Cutscenes e variações:** ao trocar um personagem da história (ex.: Big Smoke), o programa troca junto a versão de
  cutscene (`cssmoke`) e as variações de missão (`smokev`…). Dá para desmarcar as que não quiser.
- **Modos de instalação:**
  - **Mod Loader** (recomendado, usado automaticamente se o `modloader.asi` existir): grava em
    `modloader\SaSkinManager\<modelo>\`. Os `.img` originais não são alterados.
  - **IMG direto:** substitui as entradas dentro do `.img`. Antes da primeira alteração ele faz um backup completo
    (`gta3.img.skinmanager.bak`) e guarda o original de cada arquivo trocado para o *Desfazer*.
- **Trocas ativas:** agrupadas por personagem, com *Desfazer tudo*, a lista dos modelos de cada troca e *Restaurar tudo*.
- **Filtros:** *Todos*, *Trocados*, *Não trocados* e *★ Favoritos* (marque com a estrela).
- **Atalhos:** `Ctrl+F` busca, `Enter` troca, `Ctrl+Z` desfaz a última troca.
- **Configurações:** idioma (português / English), cor de destaque (laranja, verde, amarelo, roxo, azul, rosa),
  modo de instalação padrão, grade ou lista, tamanho das miniaturas, mostrar as cutscenes na lista, prévia 3D e giro,
  trocar a pasta do jogo e limpar o cache.

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

## 🎨 Procura-se um ícone!

O ícone atual (o guepardo) é só **provisório**. Se você quiser criar o ícone oficial do SA Skin Manager,
abra uma [Issue](../../issues) com a sua sugestão (PNG quadrado, de preferência 256×256 ou maior).
Sugestões são muito bem-vindas!

## Compilar

```bash
dotnet build
dotnet test
dotnet publish src/SaSkinManager.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## Estrutura

- `src/SaSkinManager.Core`: formatos (IMG VER2, IDE, TXD/DXT, geometria DFF), catálogo de personagens, biblioteca e lógica de troca. Sem WPF, testável.
- `src/SaSkinManager.App`: interface WPF (.NET 8), prévia com o 3D do próprio WPF (sem dependências extras).
- `tests/SaSkinManager.Core.Tests`: testes xUnit com IMG/TXD sintéticos.

## Roadmap

- ~~v2: prévia 3D do modelo, lado a lado original × nova, grade de modelos girando~~ (feito na v0.4.0); perfis de trocas.
- v3: troca de veículos (carros, motos, barcos, aviões, helicópteros…) via `data/vehicles.ide`.

## Licença

[GPL-3.0](LICENSE). Nenhum arquivo do jogo (Rockstar Games) é distribuído com este projeto.

## Aviso legal

O SA Skin Manager é uma ferramenta de mod não oficial, feita por fã. Não é afiliado, endossado nem patrocinado pela
Rockstar Games ou pela Take-Two Interactive. *Grand Theft Auto* e *Grand Theft Auto: San Andreas* são marcas
registradas da Take-Two Interactive Software, Inc. É preciso ter uma cópia original do jogo; o programa só lê e altera
os arquivos da cópia instalada no seu computador.
