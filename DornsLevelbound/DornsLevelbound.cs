using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace DornsLevelbound
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("WackyMole.EpicMMOSystem", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "Dorn.Levelbound",
            Name = "Levelbound",
            Version = "0.4.1";
        internal static Plugin Instance;
        static readonly ConfigSync Sync = new ConfigSync(Guid)
        {
            DisplayName = Name,
            CurrentVersion = Version,
            MinimumRequiredVersion = Version,
            ModRequired = true,
        };
        static readonly CustomSyncedValue<Dictionary<string, string>> YamlData =
            new CustomSyncedValue<Dictionary<string, string>>(Sync, "Levelbound YAML");
        static readonly Heightmap.Biome[] Progression =
        {
            Heightmap.Biome.Meadows,
            Heightmap.Biome.BlackForest,
            Heightmap.Biome.Swamp,
            Heightmap.Biome.Mountain,
            Heightmap.Biome.Plains,
            Heightmap.Biome.Mistlands,
            Heightmap.Biome.AshLands,
            Heightmap.Biome.DeepNorth,
        };
        readonly Dictionary<Heightmap.Biome, BiomeRule> cfgBiomes =
            new Dictionary<Heightmap.Biome, BiomeRule>();
        readonly Dictionary<string, Heightmap.Biome> itemBiomes = new Dictionary<
            string,
            Heightmap.Biome
        >(StringComparer.OrdinalIgnoreCase);
        ConfigEntry<bool> locked,
            admin,
            generate,
            perBiome,
            diagnostic;
        ConfigEntry<bool> showBiomeWarning;
        ConfigEntry<string> blockedActionMessage;
        ConfigEntry<float> blockedActionDuration, blockedActionCooldown;
        BiomeRule global;
        readonly Dictionary<Heightmap.Biome, ConfigEntry<int>> globalLevels =
            new Dictionary<Heightmap.Biome, ConfigEntry<int>>();
        FileSystemWatcher watcher;
        DateTime lastLoad = DateTime.MinValue;
        MethodInfo epicLevel;
        int cachedLevel;
        float nextLevel,
            nextBiomeMessage,
            nextItemMessage;
        string Folder
        {
            get { return Path.Combine(Paths.ConfigPath, "Levelbound"); }
        }
        string Biomes
        {
            get { return Path.Combine(Folder, "biomas.yml"); }
        }
        string Helper
        {
            get { return Path.Combine(Folder, "todos-itens-equipaveis.yml"); }
        }

        void Awake()
        {
            Instance = this;
            Directory.CreateDirectory(Folder);
            BindAll();
            CreateFiles();
            YamlData.ValueChanged += DebouncedLoad;
            if (Sync.IsSourceOfTruth)
                YamlData.AssignLocalValue(ReadYaml());
            Watch();
            DebouncedLoad();
            new Harmony(Guid).PatchAll();
            Logger.LogInfo("Levelbound " + Version + " carregado.");
        }

        void OnDestroy()
        {
            if (watcher != null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
        }

        ConfigEntry<T> Bind<T>(string s, string k, T d, string text)
        {
            var e = Config.Bind(s, k, d, text);
            Sync.AddConfigEntry(e);
            return e;
        }

        BiomeRule AddRule(string section, int level, bool includeLevel = true)
        {
            var rule = new BiomeRule
            {
                Level = Bind(
                    section,
                    "NivelMinimo",
                    level,
                    "Nível mínimo do EpicMMO. Use 0 para desativar."
                ),
                Area = Bind(
                    section,
                    "BloquearAcessoAoBioma",
                    true,
                    "Bloqueia o acesso ao bioma abaixo do nível mínimo."
                ),
                Craft = Bind(section, "BloquearCriacao", true, "Bloqueia criar e melhorar itens."),
                Equip = Bind(section, "BloquearEquipar", true, "Bloqueia equipar itens."),
                Consume = Bind(section, "BloquearConsumir", true, "Bloqueia consumir itens."),
            };
            if (!includeLevel)
            {
                Config.Remove(new ConfigDefinition(section, "NivelMinimo"));
                Config.Save();
            }
            foreach (Feature feature in Enum.GetValues(typeof(Feature)))
                rule.Locks[feature] = Bind(section, "Bloquear" + feature, true, "Bloqueia " + FeatureText(feature) + " abaixo do nível mínimo.");
            return rule;
        }

        void BindAll()
        {
            locked = Bind(
                "Geral",
                "TravarConfiguracaoNoServidor",
                true,
                "Impede que clientes sobrescrevam configurações do servidor."
            );
            admin = Bind(
                "Geral",
                "IgnorarRestricoesParaAdmin",
                true,
                "Administradores e host ignoram restrições."
            );
            perBiome = Bind(
                "Geral",
                "UsarRegrasPorBioma",
                true,
                "Ativo: cada bioma usa sua seção. Desativado: todos usam Regras Globais."
            );
            generate = Bind(
                "Geral",
                "GerarListaDeItensEquipaveis",
                false,
                "Gera uma única vez a lista auxiliar agrupada por mod. Ela nunca é aplicada."
            );
            diagnostic = Bind(
                "Geral",
                "ModoDiagnostico",
                false,
                "Registra regras carregadas no log."
            );
            showBiomeWarning = Bind("Mensagens", "MostrarAvisoAoEntrarNoBioma", false, "Desativado por padrão. Mensagens devem ocorrer ao tentar ações bloqueadas.");
            blockedActionMessage = Bind("Mensagens", "MensagemDeAcaoBloqueada", "<color=#ff6666>Ação bloqueada</color>: requer nível {nivel} do EpicMMO. Seu nível: {atual}.", "Use {nivel} e {atual}.");
            blockedActionDuration = Bind("Mensagens", "DuracaoDaMensagemDeAcao", 3f, "Segundos que a mensagem de ação bloqueada fica no centro da tela.");
            blockedActionCooldown = Bind("Mensagens", "IntervaloEntreMensagensDeAcao", 2f, "Intervalo mínimo, em segundos, entre mensagens de ação bloqueada.");
            global = AddRule("Regras Globais", 0, false);
            AddGlobalLevel("Meadows", Heightmap.Biome.Meadows, 1);
            AddGlobalLevel("BlackForest", Heightmap.Biome.BlackForest, 10);
            AddGlobalLevel("Swamp", Heightmap.Biome.Swamp, 20);
            AddGlobalLevel("Mountain", Heightmap.Biome.Mountain, 30);
            AddGlobalLevel("Plains", Heightmap.Biome.Plains, 40);
            AddGlobalLevel("Mistlands", Heightmap.Biome.Mistlands, 50);
            AddGlobalLevel("AshLands", Heightmap.Biome.AshLands, 60);
            AddGlobalLevel("DeepNorth", Heightmap.Biome.DeepNorth, 70);
            AddBiome("Meadows", Heightmap.Biome.Meadows, 1);
            AddBiome("BlackForest", Heightmap.Biome.BlackForest, 10);
            AddBiome("Swamp", Heightmap.Biome.Swamp, 20);
            AddBiome("Mountain", Heightmap.Biome.Mountain, 30);
            AddBiome("Plains", Heightmap.Biome.Plains, 40);
            AddBiome("Mistlands", Heightmap.Biome.Mistlands, 50);
            AddBiome("AshLands", Heightmap.Biome.AshLands, 60);
            AddBiome("DeepNorth", Heightmap.Biome.DeepNorth, 70);
            Sync.AddLockingConfigEntry(locked);
        }

        void AddBiome(string name, Heightmap.Biome biome, int level)
        {
            int order = Array.IndexOf(Progression, biome) + 1;
            cfgBiomes[biome] = AddRule("Biomas " + order.ToString("D2") + " - " + name, level);
        }

        void AddGlobalLevel(string name, Heightmap.Biome biome, int level)
        {
            globalLevels[biome] = Bind(
                "Regras Globais - Niveis por Bioma",
                "Nivel" + name,
                level,
                "Nível mínimo do EpicMMO para " + name + ". Use 0 para desativar."
            );
        }

        void CreateFiles()
        {
            if (File.Exists(Biomes))
                return;
            var s = new StringBuilder(
                "# Levelbound: este YAML SOMENTE associa o nome do prefab a um bioma.\n# Níveis e todos os bloqueios ficam no CFG e são herdados pelo item.\n# A ordem abaixo é a progressão do jogo; não altere os nomes das oito seções.\n# Referencia é uma área livre para consulta e NUNCA aplica regras.\n\nReferencia:\n  ExemploDeMod:\n    - SwordIron\n\n"
            );
            foreach (var b in Progression)
                s.Append(BiomeName(b)).Append(":\n#  - NomeDoPrefab\n");
            File.WriteAllText(Biomes, s.ToString());
        }

        Dictionary<string, string> ReadYaml()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(Biomes))
                d[Biomes] = File.ReadAllText(Biomes);
            return d;
        }

        void Watch()
        {
            try
            {
                watcher = new FileSystemWatcher(Folder, "biomas.yml");
                watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
                watcher.Changed += Changed;
                watcher.Created += Changed;
                watcher.Deleted += Changed;
                watcher.Renamed += Changed;
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Logger.LogWarning("Não foi possível monitorar YAML: " + e.Message);
            }
        }

        void Changed(object o, FileSystemEventArgs e)
        {
            if (!Sync.IsSourceOfTruth)
                return;
            try
            {
                YamlData.AssignLocalValue(ReadYaml());
                DebouncedLoad();
            }
            catch (Exception x)
            {
                Logger.LogWarning("Falha ao ler YAML: " + x.Message);
            }
        }

        void DebouncedLoad()
        {
            if ((DateTime.UtcNow - lastLoad).TotalMilliseconds < 300)
                return;
            lastLoad = DateTime.UtcNow;
            LoadRules();
        }

        void LoadRules()
        {
            itemBiomes.Clear();
            string text;
            var data = YamlData.Value;
            if (data != null && data.TryGetValue(Biomes, out text))
                ParseBiomes(text);
            if (diagnostic.Value)
                Logger.LogInfo("Associações carregadas: " + itemBiomes.Count + " itens.");
        }

        static string V(string s)
        {
            int colon = s.IndexOf(':');
            return colon < 0 ? "" : s.Substring(colon + 1).Trim().Trim('"', '\'');
        }

        static string BiomeName(Heightmap.Biome b)
        {
            return b == Heightmap.Biome.BlackForest ? "BlackForest"
                : b == Heightmap.Biome.AshLands ? "AshLands"
                : b.ToString();
        }

        static bool TryBiome(string text, out Heightmap.Biome biome)
        {
            foreach (var b in Progression)
                if (string.Equals(BiomeName(b), text, StringComparison.OrdinalIgnoreCase))
                {
                    biome = b;
                    return true;
                }
            biome = Heightmap.Biome.None;
            return false;
        }

        void ParseBiomes(string yaml)
        {
            Heightmap.Biome current = Heightmap.Biome.None;
            foreach (
                string row in yaml.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                string line = row.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                if (!line.StartsWith("-") && line.EndsWith(":"))
                {
                    Heightmap.Biome b;
                    current = TryBiome(line.Substring(0, line.Length - 1).Trim(), out b)
                        ? b
                        : Heightmap.Biome.None;
                    continue;
                }
                if (current == Heightmap.Biome.None || !line.StartsWith("-"))
                    continue;
                string prefab = line.Substring(1).Trim();
                if (prefab.StartsWith("PrefabName:"))
                    prefab = V(prefab);
                if (!string.IsNullOrEmpty(prefab))
                    itemBiomes[prefab] = current;
            }
        }

        internal int Level()
        {
            if (Time.time < nextLevel)
                return cachedLevel;
            nextLevel = Time.time + 1f;
            try
            {
                if (epicLevel == null)
                {
                    Type t =
                        Type.GetType("API.EpicMMOSystem_API, EpicMMOSystem")
                        ?? Type.GetType("API.EMMOS_API, EpicMMOSystem");
                    if (t != null)
                        epicLevel = t.GetMethod(
                            "GetLevel",
                            BindingFlags.Public | BindingFlags.Static
                        );
                }
                object x = epicLevel == null ? null : epicLevel.Invoke(null, null);
                cachedLevel = x is int ? (int)x : 0;
            }
            catch
            {
                cachedLevel = 0;
            }
            return cachedLevel;
        }

        internal bool Bypass()
        {
            return admin.Value
                && (ZNet.instance == null || ZNet.instance.LocalPlayerIsAdminOrHost());
        }

        BiomeRule Rule(Heightmap.Biome biome)
        {
            BiomeRule rule;
            return perBiome.Value && cfgBiomes.TryGetValue(biome, out rule) ? rule : global;
        }

        int RequiredLevel(Heightmap.Biome biome, BiomeRule rule)
        {
            ConfigEntry<int> level;
            if (!perBiome.Value && globalLevels.TryGetValue(biome, out level))
                return level.Value;
            return rule.Level == null ? 0 : rule.Level.Value;
        }

        bool ItemRule(ItemDrop.ItemData item, out BiomeRule rule, out Heightmap.Biome biome)
        {
            rule = null;
            biome = Heightmap.Biome.None;
            if (
                item == null
                || item.m_dropPrefab == null
                || !itemBiomes.TryGetValue(item.m_dropPrefab.name, out biome)
            )
                return false;
            rule = Rule(biome);
            return true;
        }

        internal void CheckBiome(Player p)
        {
            if (!showBiomeWarning.Value) return;
            if (p != Player.m_localPlayer || Bypass())
                return;
            BiomeRule r = Rule(p.GetCurrentBiome());
            int required = RequiredLevel(p.GetCurrentBiome(), r);
            if (
                !r.Area.Value
                || required <= 0
                || Level() >= required
                || Time.time < nextBiomeMessage
            )
                return;
            nextBiomeMessage = Time.time + 5;
            MessageHud.instance.ShowMessage(
                MessageHud.MessageType.Center,
                "<color=#ff6666>Área bloqueada</color>: requer nível "
                    + required
                    + " do EpicMMO. Seu nível: "
                    + Level()
                    + "."
            );
        }

        internal bool Blocked(ItemDrop.ItemData item, ActionKind action, out int requirement)
        {
            requirement = 0;
            if (Bypass())
                return false;
            BiomeRule r;
            Heightmap.Biome b;
            if (!ItemRule(item, out r, out b))
                return false;
            int required = RequiredLevel(b, r);
            if (required <= 0 || Level() >= required)
                return false;
            bool enabled =
                action == ActionKind.Craft ? r.Locks[Feature.Craft].Value
                : action == ActionKind.Equip ? r.Locks[Feature.Equipar].Value
                : r.Locks[Feature.ConsumirItem].Value;
            if (!enabled)
                return false;
            requirement = required;
            return true;
        }

        internal bool ItemLevelLocked(ItemDrop.ItemData item, out int requirement)
        {
            requirement = 0;
            if (Bypass()) return false;
            BiomeRule rule;
            Heightmap.Biome biome;
            if (!ItemRule(item, out rule, out biome)) return false;
            requirement = RequiredLevel(biome, rule);
            return requirement > 0 && Level() < requirement;
        }

        internal bool BlockItemPickup(ItemDrop.ItemData item)
        {
            int requirement;
            if (!ItemLevelLocked(item, out requirement)) return false;
            Tell(requirement);
            return true;
        }

        internal bool BlockPickupPrefab(GameObject prefab)
        {
            if (prefab == null || Bypass()) return false;
            Heightmap.Biome biome;
            if (!itemBiomes.TryGetValue(prefab.name, out biome)) return false;
            BiomeRule rule = Rule(biome);
            int requirement = RequiredLevel(biome, rule);
            if (requirement <= 0 || Level() >= requirement) return false;
            Tell(requirement);
            return true;
        }

        internal void Tell(int n)
        {
            if (Time.time < nextItemMessage)
                return;
            nextItemMessage = Time.time + Mathf.Max(0f, blockedActionCooldown.Value);
            string configuredMessage = blockedActionMessage.Value.Replace("{nivel}", n.ToString()).Replace("{atual}", Level().ToString());
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, configuredMessage, 0, null, false);
            return;
            MessageHud.instance.ShowMessage(
                MessageHud.MessageType.Center,
                "<color=#ff6666>Ação bloqueada</color>: requer nível "
                    + n
                    + " do EpicMMO. Seu nível: "
                    + Level()
                    + "."
            );
        }

        static string FeatureText(Feature feature) { return feature.ToString(); }

        IEnumerator HideCenterMessage(float duration)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, duration));
            FieldInfo field = typeof(MessageHud).GetField("m_messageCenterText", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Component text = field == null || MessageHud.instance == null ? null : field.GetValue(MessageHud.instance) as Component;
            if (text != null) text.gameObject.SetActive(false);
        }

        internal bool BlockFeature(Feature feature, Player player)
        {
            if (player == null || player != Player.m_localPlayer || Bypass()) return false;
            BiomeRule rule = Rule(player.GetCurrentBiome());
            int required = RequiredLevel(player.GetCurrentBiome(), rule);
            ConfigEntry<bool> enabled;
            if (!rule.Locks.TryGetValue(feature, out enabled) || !enabled.Value || required <= 0 || Level() >= required) return false;
            Tell(required);
            return true;
        }

        internal string RequirementText(ItemDrop.ItemData item, ActionKind action)
        {
            BiomeRule r;
            Heightmap.Biome b;
            if (!ItemRule(item, out r, out b))
                return "";
            int required = RequiredLevel(b, r);
            if (required <= 0) return "";
            int current = Level();
            string color = current >= required ? "#66ff66" : "#ff6666";
            return "\n<color="
                + color
                + ">["
                + BiomeName(b)
                + "] Requer nível EpicMMO "
                + required
                + " (seu nível: "
                + current
                + ")</color>";
        }

        string Origin(GameObject g)
        {
            try
            {
                foreach (Component c in g.GetComponents<Component>())
                {
                    if (c == null)
                        continue;
                    string n = c.GetType().Assembly.GetName().Name;
                    if (n != "assembly_valheim" && n != "UnityEngine" && n != "Assembly-CSharp")
                        return n;
                }
            }
            catch { }
            return "Valheim";
        }

        internal void Generate(ObjectDB db)
        {
            if (!generate.Value || File.Exists(Helper) || db == null)
                return;
            var valid = new HashSet<string>
            {
                "Consumable",
                "OneHandedWeapon",
                "TwoHandedWeapon",
                "TwoHandedWeaponLeft",
                "Bow",
                "Shield",
                "Helmet",
                "Chest",
                "Ammo",
                "Legs",
                "Torch",
                "Shoulder",
                "Utility",
                "Tool",
                "Hands",
                "Trinket",
            };
            var groups = new SortedDictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (GameObject g in db.m_items)
            {
                ItemDrop d = g.GetComponent<ItemDrop>();
                if (
                    d == null
                    || d.m_itemData == null
                    || d.m_itemData.m_shared == null
                    || !valid.Contains(d.m_itemData.m_shared.m_itemType.ToString())
                )
                    continue;
                string mod = Origin(g);
                List<string> list;
                if (!groups.TryGetValue(mod, out list))
                    groups[mod] = list = new List<string>();
                list.Add(g.name);
            }
            var s = new StringBuilder(
                "# Referência Levelbound: agrupada por mod de origem. NÃO é lida nem sincronizada.\n# Copie apenas o nome do prefab para uma das oito seções de biomas.yml.\n\nReferencia:\n"
            );
            foreach (var group in groups)
            {
                s.Append("  ").Append(group.Key).Append(":\n");
                group.Value.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string prefab in group.Value)
                    s.Append("    - ").Append(prefab).Append("\n");
            }
            File.WriteAllText(Helper, s.ToString());
            Logger.LogInfo("Lista auxiliar criada: " + Helper);
        }

        internal enum ActionKind
        {
            Craft,
            Equip,
            Consume,
        }

        internal enum Feature { EntradaEPermanencia, Dungeon, Teleporte, Construcao, Reparo, RemocaoDePeca, AlteracaoDeTerreno, PortaOuPortao, BauOuConteiner, Estacao, Altar, Comerciante, Mapa, MarcadorDoMapa, Craft, Melhoria, Equipar, UsarItem, ConsumirItem, RecursosDoBioma, SpawnDeCriaturas }

        class BiomeRule
        {
            public ConfigEntry<int> Level;
            public readonly Dictionary<Feature, ConfigEntry<bool>> Locks = new Dictionary<Feature, ConfigEntry<bool>>();
            public ConfigEntry<bool> Area,
                Craft,
                Equip,
                Consume;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateBiome")]
    class BiomePatch
    {
        static void Postfix(Player __instance)
        {
            if (Plugin.Instance != null)
                Plugin.Instance.CheckBiome(__instance);
        }
    }

    [HarmonyPatch(typeof(Game), "RequestRespawn")]
    class GenPatch
    {
        static void Postfix()
        {
            if (Plugin.Instance != null)
                Plugin.Instance.Generate(ObjectDB.instance);
        }
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), "IsEquipable")]
    class CanEquipPatch
    {
        static void Postfix(ItemDrop.ItemData __instance, ref bool __result)
        {
            int n;
            if (
                __result
                && Plugin.Instance != null
                && Plugin.Instance.Blocked(__instance, Plugin.ActionKind.Equip, out n)
            )
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), "EquipItem")]
    class EquipPatch
    {
        static bool Prefix(Humanoid __instance, ItemDrop.ItemData item)
        {
            int n;
            if (
                !__instance.IsPlayer()
                || Plugin.Instance == null
                || !Plugin.Instance.Blocked(item, Plugin.ActionKind.Equip, out n)
            )
                return true;
            Plugin.Instance.Tell(n);
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "CanConsumeItem")]
    class EatPatch
    {
        static void Postfix(ItemDrop.ItemData item, ref bool __result)
        {
            int n;
            if (
                __result
                && Plugin.Instance != null
                && Plugin.Instance.Blocked(item, Plugin.ActionKind.Consume, out n)
            )
            {
                __result = false;
                Plugin.Instance.Tell(n);
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    class CraftPatch
    {
        static readonly FieldInfo Selected = typeof(InventoryGui).GetField(
            "m_selectedRecipe",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        static bool Prefix(InventoryGui __instance)
        {
            if (__instance == null || Plugin.Instance == null || Selected == null)
                return true;
            object pair = Selected.GetValue(__instance);
            if (pair == null)
                return true;
            PropertyInfo p = pair.GetType().GetProperty("Recipe");
            Recipe recipe = p == null ? null : p.GetValue(pair, null) as Recipe;
            if (recipe == null || recipe.m_item == null)
                return true;
            ItemDrop d = recipe.m_item.GetComponent<ItemDrop>();
            int n;
            if (d == null || !Plugin.Instance.Blocked(d.m_itemData, Plugin.ActionKind.Craft, out n))
                return true;
            Plugin.Instance.Tell(n);
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), "GetTooltip", new Type[] { typeof(int) })]
    class TooltipPatch
    {
        static void Postfix(ItemDrop.ItemData __instance, ref string __result)
        {
            if (Plugin.Instance != null)
                __result += Plugin.Instance.RequirementText(__instance, Plugin.ActionKind.Equip);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "Pickup")]
    class PickupPatch
    {
        static bool Prefix(Humanoid __instance, GameObject __0)
        {
            if (!__instance.IsPlayer() || Plugin.Instance == null || __0 == null) return true;
            ItemDrop drop = __0.GetComponent<ItemDrop>();
            int requirement;
            if (drop == null || !Plugin.Instance.ItemLevelLocked(drop.m_itemData, out requirement)) return true;
            Plugin.Instance.Tell(requirement);
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "Interact")]
    class ItemDropInteractPatch
    {
        static bool Prefix(ItemDrop __instance, Humanoid __0)
        {
            return !__0.IsPlayer() || Plugin.Instance == null || !Plugin.Instance.BlockItemPickup(__instance.m_itemData);
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "Pickup")]
    class ItemDropPickupPatch
    {
        static bool Prefix(ItemDrop __instance, Humanoid __0)
        {
            return !__0.IsPlayer() || Plugin.Instance == null || !Plugin.Instance.BlockItemPickup(__instance.m_itemData);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    class RecipeVisualPatch
    {
        static readonly FieldInfo Selected = typeof(InventoryGui).GetField(
            "m_selectedRecipe",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        static void Postfix(InventoryGui __instance)
        {
            if (__instance == null || Plugin.Instance == null || Selected == null)
                return;
            object pair = Selected.GetValue(__instance);
            if (pair == null)
                return;
            PropertyInfo p = pair.GetType().GetProperty("Recipe");
            Recipe recipe = p == null ? null : p.GetValue(pair, null) as Recipe;
            if (recipe == null || recipe.m_item == null)
                return;
            ItemDrop d = recipe.m_item.GetComponent<ItemDrop>();
            if (d == null)
                return;
            __instance.m_recipeDecription.text += Plugin.Instance.RequirementText(
                d.m_itemData,
                Plugin.ActionKind.Craft
            );
            int n;
            if (Plugin.Instance.Blocked(d.m_itemData, Plugin.ActionKind.Craft, out n))
                __instance.m_craftButton.interactable = false;
        }
    }

    static class Guard
    {
        internal static bool Block(Plugin.Feature feature, Humanoid user)
        {
            return Plugin.Instance != null && Plugin.Instance.BlockFeature(feature, user as Player);
        }
        internal static bool BlockLocal(Plugin.Feature feature)
        {
            return Plugin.Instance != null && Plugin.Instance.BlockFeature(feature, Player.m_localPlayer);
        }
        internal static bool BlockDamage(Plugin.Feature feature, HitData hit)
        {
            if (Plugin.Instance == null) return false;
            Player player = hit == null ? null : hit.GetAttacker() as Player;
            return Plugin.Instance.BlockFeature(feature, player ?? Player.m_localPlayer);
        }
    }

    [HarmonyPatch(typeof(Teleport), "Interact")]
    class DungeonPatch { static bool Prefix(Humanoid __0) { return !Guard.Block(Plugin.Feature.Dungeon, __0); } }
    [HarmonyPatch(typeof(TeleportWorld), "Interact")]
    class PortalPatch { static bool Prefix(Humanoid __0) { return !Guard.Block(Plugin.Feature.Teleporte, __0); } }
    [HarmonyPatch(typeof(Door), "Interact")]
    class DoorPatch { static bool Prefix(Humanoid __0) { return !Guard.Block(Plugin.Feature.PortaOuPortao, __0); } }
    [HarmonyPatch(typeof(Container), "Interact")]
    class ContainerPatch { static bool Prefix(Humanoid __0) { return !Guard.Block(Plugin.Feature.BauOuConteiner, __0); } }
    [HarmonyPatch(typeof(CraftingStation), "Interact")]
    class CraftStationPatch { static bool Prefix(Humanoid __0) { return !Guard.Block(Plugin.Feature.Estacao, __0); } }
    [HarmonyPatch(typeof(CookingStation), "Interact")]
    class CookingPatch { static bool Prefix(Humanoid __0) { return !Guard.Block(Plugin.Feature.Estacao, __0); } }
    [HarmonyPatch(typeof(OfferingBowl), "Interact")]
    class AltarPatch { static bool Prefix(Humanoid __0) { return !Guard.Block(Plugin.Feature.Altar, __0); } }
    [HarmonyPatch(typeof(Trader), "Interact")]
    class TraderPatch { static bool Prefix(Humanoid __0) { return !Guard.Block(Plugin.Feature.Comerciante, __0); } }
    [HarmonyPatch(typeof(Player), "TryPlacePiece")]
    class BuildPatch { static bool Prefix(Player __instance, Piece __0) { if (__0 != null && __0.GetComponent<TerrainOp>() != null) return !Plugin.Instance.BlockFeature(Plugin.Feature.AlteracaoDeTerreno, __instance); return !Plugin.Instance.BlockFeature(Plugin.Feature.Construcao, __instance); } }
    [HarmonyPatch(typeof(Player), "Repair")]
    class RepairPatch { static bool Prefix(Player __instance) { return !Plugin.Instance.BlockFeature(Plugin.Feature.Reparo, __instance); } }
    [HarmonyPatch(typeof(Player), "RemovePiece")]
    class RemovePatch { static bool Prefix(Player __instance) { return !Plugin.Instance.BlockFeature(Plugin.Feature.RemocaoDePeca, __instance); } }
    [HarmonyPatch(typeof(Humanoid), "UseItem")]
    class UseItemPatch { static bool Prefix(Humanoid __instance) { return !Guard.Block(Plugin.Feature.UsarItem, __instance); } }
    [HarmonyPatch(typeof(Minimap), "AddPin")]
    class PinPatch { static bool Prefix() { return !Guard.BlockLocal(Plugin.Feature.MarcadorDoMapa); } }
    [HarmonyPatch(typeof(Minimap), "SetMapMode")]
    class MapPatch { static bool Prefix() { return !Guard.BlockLocal(Plugin.Feature.Mapa); } }
    [HarmonyPatch(typeof(Pickable), "Interact")]
    class PickablePatch { static bool Prefix(Pickable __instance, Humanoid __0) { return !__0.IsPlayer() || Plugin.Instance == null || !Plugin.Instance.BlockPickupPrefab(__instance.m_itemPrefab); } }
    [HarmonyPatch(typeof(PickableItem), "Interact")]
    class PickableItemPatch { static bool Prefix(PickableItem __instance, Humanoid __0) { return !__0.IsPlayer() || __instance.m_itemPrefab == null || Plugin.Instance == null || !Plugin.Instance.BlockPickupPrefab(__instance.m_itemPrefab.gameObject); } }
    [HarmonyPatch(typeof(MineRock), "Damage")]
    class MineRockPatch { static bool Prefix(HitData __0) { return !Guard.BlockDamage(Plugin.Feature.RecursosDoBioma, __0); } }
    [HarmonyPatch(typeof(MineRock5), "Damage")]
    class MineRock5Patch { static bool Prefix(HitData __0) { return !Guard.BlockDamage(Plugin.Feature.RecursosDoBioma, __0); } }
    [HarmonyPatch(typeof(TreeBase), "Damage")]
    class TreePatch { static bool Prefix(HitData __0) { return !Guard.BlockDamage(Plugin.Feature.RecursosDoBioma, __0); } }
    [HarmonyPatch(typeof(Destructible), "Damage")]
    class DestructiblePatch { static bool Prefix(HitData __0) { return !Guard.BlockDamage(Plugin.Feature.RecursosDoBioma, __0); } }
    [HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
    class CreatureSpawnerPatch { static bool Prefix() { return !Guard.BlockLocal(Plugin.Feature.SpawnDeCriaturas); } }
    [HarmonyPatch(typeof(SpawnArea), "SpawnOne")]
    class SpawnAreaPatch { static bool Prefix() { return !Guard.BlockLocal(Plugin.Feature.SpawnDeCriaturas); } }
}
