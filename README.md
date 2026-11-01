# Levelbound

[![Version](https://img.shields.io/badge/version-1.0.0-blue?style=flat-square)](https://github.com/)
[![BepInEx](https://img.shields.io/badge/BepInEx-5-yellow?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![EpicMMO](https://img.shields.io/badge/depends-EpicMMO%20System-orange?style=flat-square)](https://thunderstore.io/c/valheim/p/Prog/EpicMMO_System/)
[![Valheim](https://img.shields.io/badge/game-Valheim-8B4513?style=flat-square)](https://store.steampowered.com/app/892970/Valheim/)
[![Glitnir](https://img.shields.io/badge/servidor-Glitnir-6a0dad?style=flat-square)](https://discord.gg/g-l-i-t-n-i-r-848339987174785054)

> Mod de progressão para Valheim que vincula o acesso a itens, construções, recursos e altares ao nível do jogador no **EpicMMO System**, reforçando a progressão natural por bioma.

---

## Sumário

- [O que o mod faz](#o-que-o-mod-faz)
- [Progressão de biomas](#progressão-de-biomas)
- [Arquivos de configuração YAML](#arquivos-de-configuração-yaml)
- [Geração automática de conteúdo](#geração-automática-de-conteúdo)
- [Feedback visual](#feedback-visual)
- [Configuração CFG](#configuração-cfg)
- [Sincronização servidor–cliente](#sincronização-servidorcliente)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Compilação](#compilação)
- [Instalação](#instalação)
- [Notas importantes](#notas-importantes)

---

## O que o mod faz

> [!NOTE]
> Este mod foi desenvolvido para o servidor **[Glitnir](https://discord.gg/g-l-i-t-n-i-r-848339987174785054)**.

Levelbound lê arquivos YAML de configuração que mapeiam prefabs do jogo a um **bioma de progressão**. Cada bioma tem um nível mínimo de EpicMMO exigido (configurável). Quando um jogador tenta realizar uma ação com um prefab listado e **não possui o nível suficiente**, a ação é bloqueada e uma mensagem de feedback é exibida.

### Ações que podem ser bloqueadas

| Categoria | Ações |
|---|---|
| **Itens** (materiais, comida, ingredientes) | Coletar, craftar, melhorar, consumir |
| **Equipamentos** (armas, armaduras, ferramentas) | Coletar, craftar, melhorar, equipar |
| **Construções** (peças do martelo) | Colocar, reparar, remover |
| **Recursos do mundo** (minérios, árvores, rochas, pickables) | Interagir / causar dano |
| **Altares dos bosses** | Ativar a oferenda |

Cada categoria de bloqueio é independente e pode ser desativada individualmente na configuração.

---

## Progressão de biomas

O mod segue a ordem de progressão do Valheim vanilla. Os níveis padrão são:

| Bioma | Nível padrão |
|---|---|
| Meadows | 1 |
| Black Forest | 10 |
| Swamp | 20 |
| Mountain | 30 |
| Plains | 40 |
| Mistlands | 50 |
| AshLands | 60 |
| Deep North | 70 |

Os níveis são configuráveis na seção `[Biome Levels]` do arquivo CFG e são sincronizados com todos os clientes pelo servidor.

---

## Arquivos de configuração YAML

Os YAMLs ficam em `BepInEx/config/Levelbound/`. São criados automaticamente na primeira execução e são **sincronizados do servidor para os clientes** em tempo real.

### `materiais.yml`

Mapeia materiais, ingredientes, comidas e outros itens de inventário a um bioma.

```yaml
Meadows:
  - Wood
  - Stone

BlackForest:
  - CopperOre
  - Tin

# Itens sem receita classificável podem entrar aqui também
Unclassified:
#  - ItemSemReceita
```

### `equipamentos.yml`

Mapeia armas, armaduras, escudos, ferramentas, munições e acessórios a um bioma.

```yaml
Swamp:
  - SwordIron
  - ArmorIronChest

Unclassified:
#  - EquipamentoSemReceita
```

> A seção `Unclassified` pode conter equipamentos que não possuem receita classificável — eles **não recebem restrição de nível**.

### `construcoes.yml`

Mapeia peças do menu de construção (martelo) a um bioma.

```yaml
BlackForest:
  - fire_pit

Swamp:
  - wood_door
```

Peças listadas exibem o nível exigido diretamente na interface do martelo.

### `recursos.yml`

Mapeia recursos do mundo (árvores, minérios, rochas, pickables) a um bioma.

```yaml
BlackForest:
  - MineRock_Copper

Mountain:
  - MineRock_Silver
```

### `altares.yml`

Define níveis diretamente para altares de boss, usando o prefab do altar, da localização ou do próprio boss como chave.

```yaml
Eikthyrnir: 1
Eikthyr: 1
GDKing: 10
gd_king: 10
Bonemass: 20
Dragonqueen: 30
Dragon: 30
GoblinKing: 40
SeekerQueen: 50
FaderLocation: 60
Fader: 60
```

> Nível `0` desativa a restrição para aquele altar.

### `biomas.yml` *(legado)*

Arquivo da versão antiga do mod. Regras existentes nele são importadas automaticamente para `materiais.yml` na migração. Novas regras devem ir em `materiais.yml`.

### `todos-itens-equipaveis.yml` *(somente referência)*

Arquivo de referência gerado uma vez (com `GenerateEquipableItemList = true`), que lista todos os equipamentos registrados agrupados por mod de origem. **Nunca é lido nem sincronizado** — serve apenas como guia para popular os outros YAMLs.

---

## Geração automática de conteúdo

Com a opção `GenerateFilesAutomatically = true` no CFG, o mod executa uma análise completa do jogo após o carregamento e **preenche automaticamente** `materiais.yml`, `equipamentos.yml`, `construcoes.yml` e `recursos.yml` com todos os prefabs encontrados, inferindo o bioma de cada um a partir de:

- Vegetação e spawn de criaturas por bioma (`ZoneSystem`, `SpawnSystem`)
- Drops de recursos (`Pickable`, `MineRock`, `TreeBase`, etc.)
- Drops de criaturas (`CharacterDrop`)
- Drops de bosses (Hard Antler, Crypt Key, Wishbone, etc.)
- Materiais de base conhecidos (hardcoded para todos os biomas)
- Propagação por receitas: se um ingrediente pertence a um bioma, o produto também pertence
- Propagação por conversões: Smelter, CookingStation, Fermenter
- Análise de peças de construção e seus requisitos de materiais

A geração respeita entradas já existentes nos YAMLs — itens já listados manualmente **nunca são sobrescritos** (somente adições são feitas).

---

## Feedback visual

O mod injeta informações de nível diretamente na interface do jogo:

- **Tooltip de item** — exibe o nível exigido e o nível atual do jogador (verde se suficiente, vermelho se insuficiente).
- **Painel de craft/melhoria** — a descrição da receita mostra o requisito de nível; o botão de craft é desabilitado se o nível for insuficiente.
- **Descrição de peça de construção** — exibe o requisito de nível ao passar o cursor no modo de construção.
- **Texto de hover do altar** — exibe o nível exigido para ativar o altar.
- **Mensagem de ação bloqueada** — mensagem central configurável exibida ao tentar uma ação bloqueada (com cooldown configurável).

---

## Configuração CFG

O arquivo `BepInEx/config/Dorn.Levelbound.cfg` é gerado automaticamente e sincronizado com os clientes.

### `[General]`

| Chave | Padrão | Descrição |
|---|---|---|
| `LockConfiguration` | `true` | Impede que clientes sobrescrevam a configuração do servidor |
| `AdminBypass` | `true` | Administradores e o host ignoram todas as restrições |
| `GenerateEquipableItemList` | `false` | Gera `todos-itens-equipaveis.yml` uma vez (somente referência) |
| `GenerateFilesAutomatically` | `false` | Preenche os YAMLs automaticamente com prefabs do jogo e mods |
| `DiagnosticMode` | `false` | Loga regras carregadas e receitas não resolvidas no console |

### `[Biome Levels]`

Define o nível mínimo de EpicMMO para cada bioma. Use `0` para desativar a restrição de um bioma inteiro.

### `[Item Restrictions]`

| Chave | Padrão | Descrição |
|---|---|---|
| `BlockPickup` | `true` | Bloqueia coleta de itens de `materiais.yml` e `equipamentos.yml` |
| `BlockCrafting` | `true` | Bloqueia craft de itens listados |
| `BlockUpgrading` | `true` | Bloqueia melhoria de itens listados |
| `BlockEquipping` | `true` | Bloqueia equipar itens de `equipamentos.yml` |
| `BlockConsuming` | `true` | Bloqueia consumir itens de `materiais.yml` |

### `[Building Restrictions]`

| Chave | Padrão | Descrição |
|---|---|---|
| `BlockPlacement` | `true` | Bloqueia colocar peças de `construcoes.yml` |
| `BlockRepair` | `true` | Bloqueia reparar peças listadas |
| `BlockRemoval` | `true` | Bloqueia remover peças listadas |

### `[Resource Restrictions]`

| Chave | Padrão | Descrição |
|---|---|---|
| `BlockResources` | `true` | Bloqueia recursos de `recursos.yml` |

### `[Altar Restrictions]`

| Chave | Padrão | Descrição |
|---|---|---|
| `BlockAltars` | `true` | Bloqueia altares de `altares.yml` |

### `[Messages]`

| Chave | Padrão | Descrição |
|---|---|---|
| `BlockedActionMessage` | `<color=#ff6666>Action blocked</color>: requires EpicMMO level {required}. Your level: {current}.` | Mensagem exibida ao jogador. Use `{required}` e `{current}` como placeholders |
| `BlockedActionMessageCooldown` | `2` | Intervalo mínimo em segundos entre mensagens de ação bloqueada |

---

## Sincronização servidor–cliente

O mod usa a biblioteca **ServerSync** embutida na DLL. Ao conectar em um servidor:

- Toda a configuração CFG é enviada do servidor para os clientes
- Todo o conteúdo dos arquivos YAML é sincronizado em tempo real
- Clientes não podem alterar configurações se `LockConfiguration = true`
- Alterações nos YAMLs ou no CFG do servidor são recarregadas e reenviadas automaticamente (hot reload via `FileSystemWatcher`)
- Em servidores dedicados, o nível EpicMMO de cada jogador é lido diretamente do ZDO (`epicmmolevel`), pois a API pública do EpicMMO só expõe o nível do jogador local

---

## Estrutura do projeto

```
Levelbound/
├── DornsLevelbound/
│   └── DornsLevelbound.cs          # Código-fonte principal do mod
├── ServerSync/
│   ├── ConfigSync.cs               # Biblioteca ServerSync (MIT)
│   └── ServerSync.csproj
├── outputs/
│   └── Levelbound.dll              # DLL final (com ServerSync embutido)
├── pack-levelbound.proj            # Script MSBuild para mesclar ServerSync via ILRepack
└── README.md
```

---

## Compilação

O projeto é compilado contra a instalação local do Valheim e BepInEx 5.

### Passos

1. **Compile o ServerSync:**
   ```
   msbuild ServerSync\ServerSync.csproj /p:Configuration=Release
   ```

2. **Compile o mod principal** (`DornsLevelbound.cs`) referenciando:
   - `BepInEx.dll` (BepInEx 5)
   - `assembly_valheim.dll`
   - `0Harmony.dll`
   - `UnityEngine.dll` e módulos necessários
   - `ServerSync.dll` (compilado no passo anterior)

3. **Mescle ServerSync na DLL final com ILRepack:**
   ```
   msbuild pack-levelbound.proj /t:Pack
   ```
   Isso gera `outputs/Levelbound.dll` com o ServerSync internalizado.

### Dependência de build

O arquivo `pack-levelbound.proj` usa o pacote NuGet `ILRepack.Lib.MSBuild.Task` para incorporar o ServerSync como dependência interna, eliminando a necessidade de distribuir a DLL separadamente.

---

## Instalação

1. Instale o [EpicMMO System](https://thunderstore.io/c/valheim/p/Prog/EpicMMO_System/)
2. Copie `Levelbound.dll` para `BepInEx/plugins/`
3. Inicie o jogo/servidor — os arquivos YAML e o CFG serão criados automaticamente em `BepInEx/config/Levelbound/`
4. Configure os YAMLs e o CFG conforme desejado; as mudanças são recarregadas em tempo real

---

## Notas importantes

- **Admin bypass:** O host e administradores do servidor nunca são bloqueados (quando `AdminBypass = true`). A verificação de admin usa `ZNet.IsAdmin()` no servidor dedicado, impedindo que clientes se auto-concedam bypass.
- **Seção `Reference` dos YAMLs:** Entradas dentro de `Reference:` nunca são aplicadas como regras. Use para anotar ou consultar prefabs sem ativar restrições.
- **Nomes de seção dos biomas:** Os oito nomes de seção (`Meadows`, `BlackForest`, `Swamp`, `Mountain`, `Plains`, `Mistlands`, `AshLands`, `DeepNorth`) **não devem ser renomeados** nos YAMLs, pois são reconhecidos pelo parser do mod.
- **Geração automática:** `GenerateFilesAutomatically` deve ser ativado apenas uma vez para popular os arquivos. Depois disso, é recomendável desativar a opção para evitar processamento desnecessário a cada carregamento de mundo.

---

*Desenvolvido por Dorn para o servidor [Glitnir](https://discord.com/invite/g-l-i-t-n-i-r-848339987174785054).*

