using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.JsonData.EiAfxConfig;
using Ei;
using Humanizer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Ei.MissionInfo.Types;
using System.Text.Json;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace EGG9000.Common.Helpers {

    public class InventoryArtifactCount {
        public ArtifactCount AF { get; set; }
        public bool Skip { get; set; } = false;
    }

    public static class ArtifactHelpers {

        private class MennoAPIData {
            public string shipTypeName { get; set; }
            public Spaceship Ship {
                get {
                    if(!Enum.TryParse<Spaceship>(shipTypeName, ignoreCase: true, out var spaceship)) {
                        return Spaceship.ChickenOne;
                    }
                    return spaceship;
                }
            }
            public string shipDurationTypeName { get; set; }
            public DurationType DurationType {
                get {
                    if(!Enum.TryParse<DurationType>(shipDurationTypeName, ignoreCase: true, out var durationType)) {
                        return DurationType.Tutorial;
                    }
                    return durationType;
                }
            }
            public int shipLevel { get; set; }
            public double shipsNeededPerLegendary { get; set; }
        }

        private static HttpClient _httpClient;
        private static readonly string MennoAPIURL = "https://eggincdatacollection.azurewebsites.net/";
        private static readonly string APIEndpoint = "api/GetLLCData";
        private static readonly string MennoDataKey = "MennoDataCache";
        public static async Task<List<(Spaceship ship, DurationType type, List<double> legendaryDropRates)>> GetShipDataTable(IMemoryCache _cache, ILogger _logger) {
            if(!_cache.TryGetValue(MennoDataKey, out List<(Spaceship ship, DurationType type, List<double> legendaryDropRates)> mennoData)) {
                mennoData = await GetNewMennoData(_logger);
                _cache.Set(MennoDataKey, mennoData, TimeSpan.FromHours(6));
            }

            return mennoData;
        }

        public static uint GetCraftingLevel(double CraftingXP) {
            uint currentLevel = 1;
            var xpThresholds = Root.Get().craftingLevelXpThresholds;
            for(var i = xpThresholds.Count - 1; i >= 0; i--) {
                if(CraftingXP >= xpThresholds[i]) {
                    currentLevel = (uint)i + 1;
                    break;
                }
            }
            return currentLevel;
        }

        public static uint GetCraftingLevel(this CustomBackup backup) {
            return GetCraftingLevel(backup.CraftingXP);
        }

        private static async Task<List<(Spaceship ship, DurationType type, List<double> legendaryDropRates)>> GetNewMennoData(ILogger _logger) {
            _httpClient ??= new() {
                BaseAddress = new Uri(MennoAPIURL)
            };
            //Dispose of any junk requests left over, prevent mem leaks
            _httpClient.CancelPendingRequests();
            try {
                var response = await _httpClient.GetAsync(APIEndpoint);
                response.EnsureSuccessStatusCode();
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var shipDataArray = JsonConvert.DeserializeObject<MennoAPIData[]>(jsonResponse);

                _logger.LogInformation("Menno Ship Coefficients were refreshed at {refreshTime}, and will be invalidated again at {invalidationTime}", DateTimeOffset.UtcNow.Humanize(), DateTimeOffset.UtcNow.AddHours(6).Humanize());

                return shipDataArray.GroupBy(data => new { data.Ship, data.DurationType })
                    .SelectMany(shipGrouping => shipGrouping.GroupBy(sg => sg.Ship)
                        .Select(durationGrouping => (
                            ship: durationGrouping.First().Ship,
                            type: durationGrouping.First().DurationType,
                            legendaryDropRates: durationGrouping
                                .OrderBy(d => d.shipLevel)
                                .Select(d => d.shipsNeededPerLegendary)
                                .ToList()
                        )
                    )
                ).ToList();
            } catch(HttpRequestException ex) {
                _logger.LogError("Failed to load Menno Ship Coefficients from API: {exception}", ex);
                return null;
            }
        }

        public static string GetArtifactFairnessScoreString(List<ArtifactCount> ArtifactHall) {
            return (ArtifactHall is null || ArtifactHall.Count == 0) ? "0 (null artifact hall)" : GetArtifactFairnessScore(ArtifactHall).ToString("E");
        }

        public static BigInteger GetArtifactFairnessScore(List<ArtifactCount> ArtifactHall) {
            if(ArtifactHall is null || ArtifactHall.Count == 0) return 0;
            // Collapse duplicate rows for the same artifact instance before scoring. A real hall has one
            // row per distinct artifact; stray duplicates (from a corrupted/legacy backup) would otherwise
            // be summed separately and massively inflate the score, producing false cheater flags.
            var collapsed = ArtifactHall
                .Where(a => a.Artifact is not null)
                .GroupBy(a => a.Artifact)
                .Select(g => (Artifact: g.Key, Count: g.Sum(x => x.Count)));
            return (BigInteger)collapsed.Sum(a => Math.Pow(GetFairness(a.Artifact)[a.Artifact.Tier - 1], a.Artifact.Rarity + 1) * a.Count);
        }

        public static int GetAFOrder(string AF) {
            return AF switch {
                "Aurelian Brooch" => 18,
                "Beak of Midas" => 23,
                "Book of Basan" => 33,
                "Carved Rainstick" => 24,
                "Clarity Stone" => 12,
                "Demeters Necklace" => 16,
                "Dilithium Monocle" => 29,
                "Dilithium Stone" => 11,
                "Gold Meteorite" => 1,
                "Gusset" => 20,
                "Ornate Gusset" => 20,
                "Interstellar Compass" => 25,
                "Life Stone" => 10,
                "Light of Eggendil" => 34,
                "Lunar Stone" => 5,
                "Lunar Totem" => 15,
                "Mercury's Lens" => 22,
                "Neodymium Medallion" => 21,
                "Phoenix Feather" => 27,
                "Prophecy Stone" => 13,
                "Puzzle Cube" => 14,
                "Quantum Metronome" => 28,
                "Quantum Stone" => 9,
                "Shell Stone" => 4,
                "Ship in a Bottle" => 31,
                "Solar Titanium" => 3,
                "Soul Stone" => 8,
                "Tachyon Deflector" => 32,
                "Tachyon Stone" => 6,
                "Tau Ceti Geode" => 2,
                "Terra Stone" => 7,
                "The Chalice" => 26,
                "Titanium Actuator" => 30,
                "Tungsten Ankh" => 19,
                "Vial of Martian Dust" => 17,
                _ => 0,
            };
        }

        public static long[] GetFairness(EggIncArtifactInstance instance) {
            return (instance is null || instance.Artifact is null || instance.Tier < 0) ? new long[] { 0, 0, 0, 0 } : instance.Artifact.Replace(" Fragement", "") switch {
                "Aurelian Brooch" => new long[] { 0, 1186, 13827, 58753 },
                "Beak of Midas" => new long[] { 0, 6075, 23885, 86083 },
                "Book of Basan" => new long[] { 0, 114405, 360427, 934701 },
                "Carved Rainstick" => new long[] { 0, 10082, 39572, 168121 },
                "Clarity Stone" => new long[] { 0, 36603, 185026, 774986 },
                "Demeters Necklace" => new long[] { 0, 383, 7628, 41267 },
                "Dilithium Monocle" => new long[] { 0, 24487, 80590, 271970 },
                "Dilithium Stone" => new long[] { 0, 19671, 82572, 459362 },
                "Gusset" => new long[] { 0, 5167, 28986, 163774 },
                "Interstellar Compass" => new long[] { 0, 9425, 37923, 226570 },
                "Life Stone" => new long[] { 0, 10507, 63177, 325027 },
                "Light of Eggendil" => new long[] { 0, 67892, 168121, 330068 },
                "Lunar Stone" => new long[] { 0, 406, 12447, 155125 },
                "Lunar Totem" => new long[] { 1, 1, 4749, 28986 },
                "Mercury's Lens" => new long[] { 0, 7084, 30365, 295510 },
                "Neodymium Medallion" => new long[] { 0, 3982, 16466, 58753 },
                "Phoenix Feather" => new long[] { 0, 16466, 50473, 237296 },
                "Prophecy Stone" => new long[] { 0, 50259, 246536, 731674 },
                "Puzzle Cube" => new long[] { 0, 90, 14672, 110353 },
                "Quantum Metronome" => new long[] { 0, 18400, 67892, 306621 },
                "Quantum Stone" => new long[] { 0, 7252, 25118, 228278 },
                "Shell Stone" => new long[] { 0, 262, 18048, 183125 },
                "Ship in a Bottle" => new long[] { 0, 36322, 113895, 442510 },
                "Soul Stone" => new long[] { 0, 5196, 64770, 250022 },
                "Tachyon Deflector" => new long[] { 0, 77412, 259516, 1220591 },
                "Tachyon Stone" => new long[] { 0, 495, 18048, 236946 },
                "Terra Stone" => new long[] { 0, 5196, 29988, 303837 },
                "The Chalice" => new long[] { 0, 8798, 30365, 139253 },
                "Titanium Actuator" => new long[] { 0, 26353, 88920, 215900 },
                "Tungsten Ankh" => new long[] { 0, 3982, 25099, 110546 },
                "Vial of Martian Dust" => new long[] { 0, 3302, 26353, 139253 },
                _ => new long[] { 0, 0, 0, 0 }
            };
        }

        public static string GetRarityEmoji(EggIncArtifactInstance instance) {
            return (instance is null) ? "" : instance.Rarity switch {
                1 => "",
                2 => "<:Rare:905959988030226453>",
                3 => "<:Epic:905960149720649748>",
                4 => "<:Legendary:905960165860339722>",
                _ => ""
            };
        }

        public class ShipTargetInfo() {
            public string Name { get; set; }
            public string EmojiURL { get; set; }
        }

        public static ShipTargetInfo GetTargetInfo(ArtifactSpec.Types.Name aspec) {
            var bInfo = string.Join(" ", Regex.Split(Enum.GetName(typeof(ArtifactSpec.Types.Name), aspec), @"(?=\p{Lu})"))
                .Replace(" Of ", " of ").Replace(" In ", " in ").Replace(" A ", " a ").Trim();
            var tier = bInfo.IndexOf("Fragment") > -1 ? (byte)0
                    : (bInfo.IndexOf(" Stone") > -1 || bInfo.IndexOf("Tau Ceti Geode") > -1 || bInfo.IndexOf("Gold Meteorite") > -1 || bInfo.IndexOf("Solar Titanium") > -1 ? (byte)3
                    : (byte)4);
            var tempEmoji = GetAfEmoji(new EggIncArtifactInstance() {
                Id = (byte)(int)aspec,
                Tier = tier
            });
            return new ShipTargetInfo() {
                Name = bInfo,
                EmojiURL = tempEmoji == bInfo ? "" : $"https://cdn.discordapp.com/emojis/{tempEmoji.Split(":")[2].Replace(">", "")}"
            };
        }

        public static string GetAfEmoji(EggIncArtifactInstance instance) {
            return (instance is null || instance.Artifact is null || instance.Tier < 0) ? "" : instance.Artifact switch {
                "Aurelian Brooch" => new string[] { "<:Afx_Aurelian_Brooch_1:801924296213659720>", "<:Afx_Aurelian_Brooch_2:801924317109288981>", "<:Afx_Aurelian_Brooch_3:801924329759571992>", "<:Afx_Aurelian_Brooch_4:801400352338739210>" }[instance.Tier - 1],
                "Beak of Midas" => new string[] { "<:Afx_Beak_Of_Midas_1:801923763141738527>", "<:Afx_Beak_Of_Midas_2:801923773669441546>", "<:Afx_Beak_Of_Midas_3:801923785963601941>", "<:Afx_Beak_Of_Midas_4:801400369145970688>" }[instance.Tier - 1],
                "Book of Basan" => new string[] { "<:Afx_Book_Of_Basan_1:801924039102562375>", "<:Afx_Book_Of_Basan_2:801924052922925096>", "<:Afx_Book_Of_Basan_3:801924064180174931>", "<:Afx_Book_Of_Basan_4:801400385230733333>" }[instance.Tier - 1],
                "Carved Rainstick" => new string[] { "<:Afx_Carved_Rainstick_1:801940593416077353>", "<:Afx_Carved_Rainstick_2:801940608453050438>", "<:Afx_Carved_Rainstick_3:801940622927462480>", "<:Afx_Carved_Rainstick_4:801400417979990066>" }[instance.Tier - 1],
                "Clarity Stone" => new string[] { "<:Afx_Clarity_Stone_1:807798847073550366>", "<:Afx_Clarity_Stone_2:807798825163030528>", "<:Afx_Clarity_Stone_3:807798807852613692>", "<:Afx_Clarity_Stone_4:801400496946282526>" }[instance.Tier],
                "Demeters Necklace" => new string[] { "<:Afx_Demeters_Necklace_1:801791558407159829>", "<:Afx_Demeters_Necklace_2:801791575348609034>", "<:Afx_Demeters_Necklace_3:801791588988092446>", "<:Afx_Demeters_Necklace_4:801400518710788096>" }[instance.Tier - 1],
                "Dilithium Monocle" => new string[] { "<:Afx_Dilithium_Monocle_1:801786173390979122>", "<:Afx_Dilithium_Monocle_2:801786191748923403>", "<:Afx_Dilithium_Monocle_3:801786210266775562>", "<:Afx_Dilithium_Monocle_4:801402979470278656>" }[instance.Tier - 1],
                "Dilithium Stone" => new string[] { "<:Afx_Dilithium_Stone_1:807798908897591296>", "<:Afx_Dilithium_Stone_2:807798889901588490>", "<:Afx_Dilithium_Stone_3:807798875153891368>", "<:Afx_Dilithium_Stone_4:801400591363342380>" }[instance.Tier],
                "Gold Meteorite" => new string[] { "<:Afx_Gold_Meteorite_1:807766396712910898>", "<:Afx_Gold_Meteorite_2:807766382317404171>", "<:Afx_Gold_Meteorite_3:801400672737034240>" }[instance.Tier - 1],
                "Gusset" => new string[] { "<:Afx_Ornate_Gusset_1:801790964679180339>", "<:Afx_Ornate_Gusset_2:801790982563561502>", "<:Afx_Ornate_Gusset_3:801790999697293342>", "<:Afx_Ornate_Gusset_4:801400934236160011>" }[instance.Tier - 1],
                "Interstellar Compass" => new string[] { "<:Afx_Interstellar_Compass_1:801946444600311818>", "<:Afx_Interstellar_Compass_2:801946468734861322>", "<:Afx_Interstellar_Compass_3:801946486627106817>", "<:Afx_Interstellar_Compass_4:801402991776104478>" }[instance.Tier - 1],
                "Life Stone" => new string[] { "<:Afx_Life_Stone_1:807798970792935426>", "<:Afx_Life_Stone_2:807798955810488321>", "<:Afx_Life_Stone_3:807798940539944990>", "<:Afx_Life_Stone_4:801400782557151252>" }[instance.Tier],
                "Light of Eggendil" => new string[] { "<:Afx_Light_Of_Eggendil_1:801948939296702476>", "<:Afx_Light_Of_Eggendil_2:801790900110884874>", "<:Afx_Light_Of_Eggendil_3:801790909250404382>", "<:Afx_Light_Of_Eggendil_4:801400809551560734>" }[instance.Tier - 1],
                "Lunar Stone" => new string[] { "<:Afx_Lunar_Stone_1:807799032306860062>", "<:Afx_Lunar_Stone_2:807799013062606850>", "<:Afx_Lunar_Stone_3:807798994167922690>", "<:Afx_Lunar_Stone_4:801400832640679946>" }[instance.Tier],
                "Lunar Totem" => new string[] { "<:Afx_Lunar_Totem_1:801923971880321085>", "<:Afx_Lunar_Totem_2:801923990872522752>", "<:Afx_Lunar_Totem_3:801924010203938817>", "<:Afx_Lunar_Totem_4:801400854037004289>" }[instance.Tier - 1],
                "Mercury's Lens" => new string[] { "<:Afx_Mercurys_Lens_1:801950726909722665>", "<:Afx_Mercurys_Lens_2:801950753585365034>", "<:Afx_Mercurys_Lens_3:801403003185266708>", "<:Afx_Mercurys_Lens_4:801400885149564928>" }[instance.Tier - 1],
                "Neodymium Medallion" => new string[] { "<:Afx_Neo_Medallion_1:801923701162901504>", "<:Afx_Neo_Medallion_2:801923722973413467>", "<:Afx_Neo_Medallion_3:801923743894863872>", "<:Afx_Neo_Medallion_4:801400910060060673>" }[instance.Tier - 1],
                "OrnateGusset" => new string[] { "<:Afx_Ornate_Gusset_1:801790964679180339>", "<:Afx_Ornate_Gusset_2:801790982563561502>", "<:Afx_Ornate_Gusset_3:801790999697293342>", "<:Afx_Ornate_Gusset_4:801400934236160011>" }[instance.Tier - 1],
                "Phoenix Feather" => new string[] { "<:Afx_Phoenix_Feather_1:801924123302428702>", "<:Afx_Phoenix_Feather_2:801924144534519818>", "<:Afx_Phoenix_Feather_3:801924163022749736>", "<:Afx_Phoenix_Feather_4:801403016195473418>" }[instance.Tier - 1],
                "Prophecy Stone" => new string[] { "<:Afx_Prophecy_Stone_1:807799478722887680>", "<:Afx_Prophecy_Stone_2:807799450608205905>", "<:Afx_Prophecy_Stone_3:807799057384341521>", "<:Afx_Prophecy_Stone_4:801400987809873971>" }[instance.Tier],
                "Puzzle Cube" => new string[] { "<:Afx_Puzzle_Cube_1:801954137486655589>", "<:Afx_Puzzle_Cube_2:801954163926761522>", "<:Afx_Puzzle_Cube_3:801954186517413918>", "<:Afx_Puzzle_Cube_4:801401010718638080>" }[instance.Tier - 1],
                "Quantum Metronome" => new string[] { "<:Afx_Quantum_Metronome_1:801923609483804694>", "<:Afx_Quantum_Metronome_2:801923633424760863>", "<:Afx_Quantum_Metronome_3:801923659220123698>", "<:Afx_Quantum_Metronome_4:801401052720922634>" }[instance.Tier - 1],
                "Quantum Stone" => new string[] { "<:Afx_Quantum_Stone_1:807799549954883624>", "<:Afx_Quantum_Stone_2:807799527498711040>", "<:Afx_Quantum_Stone_3:807799509630582854>", "<:Afx_Quantum_Stone_4:801401076712079381>" }[instance.Tier],
                "Shell Stone" => new string[] { "<:Afx_Shell_Stone_1:807799916881117214>", "<:Afx_Shell_Stone_2:807799894089007145>", "<:Afx_Shell_Stone_3:807799866142490644>", "<:Afx_Shell_Stone_4:801401100573343776>" }[instance.Tier],
                "Ship in a Bottle" => new string[] { "<:Afx_Ship_In_A_Bottle_1:801955542238363688>", "<:Afx_Ship_In_A_Bottle_2:801955569858641981>", "<:Afx_Ship_In_A_Bottle_3:801955999123636244>", "<:Afx_Ship_In_A_Bottle_4:801746240785481740>" }[instance.Tier - 1],
                "Solar Titanium" => new string[] { "<:Afx_Solar_Titanium_1:807261434039894086>", "<:Afx_Solar_Titanium_2:807261357182550067>", "<:Afx_Solar_Titanium_3:801401141605695529>" }[instance.Tier - 1],
                "Soul Stone" => new string[] { "<:Afx_Soul_Stone_1:807800048662085663>", "<:Afx_Soul_Stone_2:807800028634415144>", "<:Afx_Soul_Stone_3:807799997764075521>", "<:Afx_Soul_Stone_4:801401175487807519>" }[instance.Tier],
                "Tachyon Deflector" => new string[] { "<:Afx_Tachyon_Deflector_1:801956755655229510>", "<:Afx_Tachyon_Deflector_2:801956779272437800>", "<:Afx_Tachyon_Deflector_3:801956823296770071>", "<:Afx_Tachyon_Deflector_4:801401224292728842>" }[instance.Tier - 1],
                "Tachyon Stone" => new string[] { "<:Afx_Tachyon_Stone_1:807800396264767528>", "<:Afx_Tachyon_Stone_2:807800375100964864>", "<:Afx_Tachyon_Stone_3:807800354380840980>", "<:Afx_Tachyon_Stone_4:801401251278880788>" }[instance.Tier],
                "Tau Ceti Geode" => new string[] { "<:Afx_Tau_Ceti_Geode_1:807766532813619240>", "<:Afx_Tau_Ceti_Geode_2:807766597225283617>", "<:Afx_Tau_Ceti_Geode_3:801401276830056488>" }[instance.Tier - 1],
                "Terra Stone" => new string[] { "<:Afx_Terra_Stone_1:807800715929583626>", "<:Afx_Terra_Stone_2:807800439831658506>", "<:Afx_Terra_Stone_3:807800418549366826>", "<:Afx_Terra_Stone_4:801401301107343390>" }[instance.Tier],
                "The Chalice" => new string[] { "<:Afx_The_Chalice_1:801923476294205480>", "<:Afx_The_Chalice_2:801923503058059287>", "<:Afx_The_Chalice_3:801923523319693342>", "<:Afx_The_Chalice_4:801401326708850698>" }[instance.Tier - 1],
                "Titanium Actuator" => new string[] { "<:Afx_Titanium_Actuator_1:801957617253351435>", "<:Afx_Titanium_Actuator_2:801957652052967474>", "<:Afx_Titanium_Actuator_3:801957676808536094>", "<:Afx_Titanium_Actuator_4:801401362763874304>" }[instance.Tier - 1],
                "Tungsten Ankh" => new string[] { "<:Afx_Tungsten_Ankh_1:801924204605866035>", "<:Afx_Tungsten_Ankh_2:801924230731399178>", "<:Afx_Tungsten_Ankh_3:801924249522012220>", "<:Afx_Tungsten_Ankh_4:801401388256591932>" }[instance.Tier - 1],
                "Vial Martian Dust" => new string[] { "<:Afx_Vial_Nartian_Dust_1:801923830023979008>", "<:Afx_Vial_Nartian_Dust_2:801923862836412427>", "<:Afx_Vial_Nartian_Dust_3:801923891534364672>", "<:Afx_Vial_Martian_Dust_4:801401410578939914>" }[instance.Tier - 1],
                "Vial of Martian Dust" => new string[] { "<:Afx_Vial_Nartian_Dust_1:801923830023979008>", "<:Afx_Vial_Nartian_Dust_2:801923862836412427>", "<:Afx_Vial_Nartian_Dust_3:801923891534364672>", "<:Afx_Vial_Martian_Dust_4:801401410578939914>" }[instance.Tier - 1],
                _ => instance.Artifact.ToString()
            };
        }

        public static uint GetTotalCraftWithLegendaryPossibility(List<ArtifactCount> artifactHall) {
            return (artifactHall is null || artifactHall.Count == 0) ? 0 : (uint)artifactHall.Where(a => (a.Artifact.Tier == 4 && a.Artifact.Artifact != "Lunar Totem") || (a.Artifact.Tier == 3 && a.Artifact.Artifact == "Tungsten Ankh")).Sum(c => c.NumberCrafted);
        }

        public static int GetLegendaryArtifactCount(List<ArtifactCount> artifactHall, bool llcCount = false) {
            //Don't account for leg totems in LLC count
            return artifactHall?.Where(a => a.Artifact?.Rarity == 4 && (!llcCount || a.Artifact.Artifact != "Lunar Totem")).Count() ?? 0;
        }

        public static string GetAfxSetString(List<EggIncArtifactInstance> set) {
            return string.Join("\n", set.Select(GetAfxString));
        }

        public static string GetAfxString(EggIncArtifactInstance instance) {
            return GetAfEmoji(instance) + GetRarityEmoji(instance) + string.Join("", instance.Stones.Select(GetAfEmoji)).ToString();
        }

        #region InventoryImages

        public static IImageProcessingContext ApplyRoundedCorners(this IImageProcessingContext context, float cornerRadius) {
            var size = context.GetCurrentSize();
            context.SetGraphicsOptions(new GraphicsOptions() {
                Antialias = true,
                AlphaCompositionMode = PixelAlphaCompositionMode.DestOut // Enforces that any part of this shape that has color is punched out of the background
            });
            BuildCorners(size.Width, size.Height, cornerRadius).ToList().ForEach(p => context = context.Fill(Color.Red, p)); //Color here is un-important, just can't be transparent
            return context;
        }

        public static IPathCollection BuildCorners(int imageWidth, int imageHeight, float cornerRadius) {
            var rect = new RectangularPolygon(-0.5f, -0.5f, cornerRadius, cornerRadius);
            var cornerTopLeft = rect.Clip(new EllipsePolygon(cornerRadius - 0.5f, cornerRadius - 0.5f, cornerRadius));
            var rightPos = imageWidth - cornerTopLeft.Bounds.Width + 1;
            var bottomPos = imageHeight - cornerTopLeft.Bounds.Height + 1;
            var cornerTopRight = cornerTopLeft.RotateDegree(90).Translate(rightPos, 0);
            var cornerBottomLeft = cornerTopLeft.RotateDegree(-90).Translate(0, bottomPos);
            var cornerBottomRight = cornerTopLeft.RotateDegree(180).Translate(rightPos, bottomPos);
            return new PathCollection(cornerTopLeft, cornerBottomLeft, cornerTopRight, cornerBottomRight);
        }

        public static Image BackgroundImage(Color backgroundColor, int size, int radius) {
            var image = new Image<Rgba32>(size, size);
            var graphicsOptions = new GraphicsOptions {
                Antialias = true,
            };
            image.Mutate(x => x.Fill(backgroundColor)); // Fill the image with a background 
            image.Mutate(x => x.ApplyRoundedCorners(radius));
            return image;
        }

        public static (int x, int y) GetPositionInGrid(int index, int rows, int columns, int itemSize, int padding) {
            if(index < 0 || index >= rows * columns) {
                throw new ArgumentException("Index is out of range for the given grid size.");
            }
            var column = index / rows;
            var row = index % rows;
            var x = column * (itemSize + padding) + padding;
            var y = row * (itemSize + padding) + padding;
            return (x, y);
        }

        public static (int rows, int columns) FindClosestGridSize(int itemCount) {
            if(itemCount <= 0) {
                throw new ArgumentException("itemCount must be a positive integer.");
            }

            var closestSquareRoot = (int)Math.Floor(Math.Sqrt(itemCount));
            var rows = closestSquareRoot;
            var columns = itemCount / rows;

            while(rows * columns < itemCount) {
                columns++;
            }

            return (rows, columns);
        }

        // Builds a valid sample artifact set from a backup: at most four artifacts, one per family (the
        // game never equips two of the same family), optionally including/excluding a deflector
        // (CoopMembersEggLayingRates). `familyOffset` rotates the family pick order so different sample
        // sets look distinct. Used to synthesise display sets (e.g. the DEV combos preview) without the
        // page hand-rolling family-uniqueness rules.
        public static List<EggIncArtifactInstance> BuildSampleSet(CustomBackup backup, bool withDeflector, int familyOffset = 0) {
            if(backup?.ArtifactHall is null) return new List<EggIncArtifactInstance>();

            var byFamily = backup.ArtifactHall
                .Where(a => a.Count > 0 && a.Artifact is not null && !a.Artifact.Artifact.Contains("Stone", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Artifact)
                .GroupBy(a => a.Artifact) // artifact name resolves 1:1 to family
                .Select(g => g.OrderByDescending(a => a.Rarity).ThenByDescending(a => a.Tier).First())
                .ToList();

            var deflectors = byFamily.Where(a => a.Boost == EGG9000.Common.JsonData.EiStatics.EggIncBoostTypeEnum.CoopMembersEggLayingRates).ToList();
            var others = byFamily.Where(a => a.Boost != EGG9000.Common.JsonData.EiStatics.EggIncBoostTypeEnum.CoopMembersEggLayingRates).ToList();

            var set = new List<EggIncArtifactInstance>();
            if(withDeflector && deflectors.Count > 0) set.Add(deflectors[0]);

            var slotsLeft = 4 - set.Count;
            for(var i = 0; i < slotsLeft && others.Count > 0; i++) {
                set.Add(others[(familyOffset + i) % others.Count]);
            }
            // Drop any accidental family duplicates from the offset wrap, then cap at four.
            return set.GroupBy(a => a.Artifact).Select(g => g.First()).Take(4).ToList();
        }

        public static List<ArtifactCount> GetOrderedInventory(EggIncAccount account) {
            if(account is null || account.Backup is null || account.Backup.ArtifactHall is null) return null;
            // Copy each ArtifactCount so collapsing duplicates never mutates the live backup. The same
            // ArtifactHall objects feed the fairness-score / cheater checks and get reused across renders;
            // writing Count back into them inflated stacks and produced false cheater pings.
            var orderedList = account.Backup.ArtifactHall.Where(a => a.Count > 0).OrderByDescending(i => i.Artifact.Rarity).Select(a => new InventoryArtifactCount() {
                AF = new ArtifactCount { Count = a.Count, Artifact = a.Artifact, NumberCrafted = a.NumberCrafted }, Skip = false
            }).ToList();
            var rarityGroupedAfs = orderedList.GroupBy(a => a.AF.Artifact.Rarity).ToList();
            orderedList = new List<InventoryArtifactCount>();
            foreach(var rarityGrouping in rarityGroupedAfs) {
                orderedList.AddRange(rarityGrouping.OrderByDescending(g => GetAFOrder(g.AF.Artifact.Artifact.Replace(" Fragment", "")) + (g.AF.Artifact.Artifact.Contains("Fragment") ? -0.05 : 0) + 0.05 * g.AF.Artifact.Tier + 0.01 * g.AF.Artifact.Stones.Count).ToList());
            }
            foreach(var acount in orderedList.Where(a => !a.Skip && a.AF.Artifact.Stones?.Count == 0)) {
                var others = orderedList.Where(a => acount.AF.Artifact.Equals(a.AF.Artifact) && orderedList.IndexOf(a) != orderedList.IndexOf(acount)).ToList();
                foreach(var other in others) other.Skip = true;
                acount.AF.Count += others.Sum(o => o.AF.Count);
            }
            return orderedList.Where(a => !a.Skip).Select(a => a.AF).ToList();
        }

        public class InventoryCreatorConfig : IRenderConfig {
            public int AFSize { get; set; } = 0;
            public int Padding { get; set; } = 0;
            public int StoneSize { get; set; } = 0;
            public int AFCornerRadius { get; set; } = 0;

            public int TextHeight { get; set; } = 0;
            public int TextBaseWidth { get; set; } = 0;
            public int TextCornerRadius { get; set; } = 0;
            public int TextFontSize { get; set; } = 0;

            public int Rows { get; set; } = 0;
            public int Columns { get; set; } = 0;

            public int TotalWidth { get; set; } = 0;
            public int TotalHeight { get; set; } = 0;

            public InventoryCreatorConfig(int afSize, int textHeight, int rows, int columns) {
                AFSize = afSize;
                Padding = AFSize / 5;
                StoneSize = (int)(AFSize / 4.5);
                AFCornerRadius = AFSize / 4;

                TextHeight = textHeight;
                TextBaseWidth = TextCornerRadius = (int)(TextHeight / 2.5);
                TextFontSize = TextBaseWidth * 2;

                Rows = rows;
                Columns = columns;

                TotalWidth = (Columns * AFSize) + (Padding * (Columns + 1));
                TotalHeight = (Rows * AFSize) + (Padding * (Rows + 1));
            }

            public bool IsValid(out string error) {
                if(AFSize <= 0) { error = "AFSize must be > 0."; return false; }
                if(Padding < 0) { error = "Padding must be >= 0."; return false; }
                if(StoneSize <= 0) { error = "StoneSize must be > 0."; return false; }
                if(AFCornerRadius < 0) { error = "AFCornerRadius must be >= 0."; return false; }
                if(TextHeight <= 0) { error = "TextHeight must be > 0."; return false; }
                if(TextFontSize <= 0) { error = "TextFontSize must be > 0."; return false; }
                if(Rows <= 0 || Columns <= 0) { error = "Rows and Columns must be > 0."; return false; }
                if(TotalWidth <= 0 || TotalHeight <= 0) { error = "TotalWidth and TotalHeight must be > 0."; return false; }
                error = null;
                return true;
            }
        }

        public class InventoryAPIObject {
            public string EID { get; set; }
            public InventoryCreatorConfig Config { get; set; }
        }

        public static async Task<(string B64, InventoryCreatorConfig Config)> InventoryB64(EggIncAccount account, bool removeB64Header = true) {
            /*
            * Constants that will determine how the image comes out
            */

            var orderedList = GetOrderedInventory(account);
            if(orderedList is null) return ("$ERROR$:orderedList is null", null);

            var (rows, columns) = FindClosestGridSize(orderedList.Count);
            var config = new InventoryCreatorConfig(100, 30, rows, columns);

            var postedObject = new InventoryAPIObject() {
                EID = account.Id,
                Config = config,
            };

            var siteApi = SiteApiClient.Create();
            using var client = siteApi.client;
            var baseUrl = siteApi.baseUrl;

            var apiUrl = $"{baseUrl}/api/generateinventoryb64";
            var jsonContent = System.Text.Json.JsonSerializer.Serialize(postedObject);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try {
                var response = await client.PostAsync(apiUrl, content);
                if(!response.IsSuccessStatusCode) return ("$ERROR$:response status code is not success", null);

                var contentType = response.Content.Headers.ContentType?.MediaType;
                // Check if the response contains an image
                if(contentType?.StartsWith("image/") != true) return ("$ERROR$:response was not an image", null);

                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                using var ms = new MemoryStream(imageBytes);
                var image = Image.Load(ms);
                var imageB64 = image.ToBase64String(JpegFormat.Instance);
                if (removeB64Header) {
                    // Remove everything up until, and including 'base64,'
                    var splits = imageB64.Split("base64,");
                    if(splits.Length > 1) imageB64 = splits[1];
                }
                return (imageB64, config);
            } catch(Exception e) {
                return ($"$ERROR$:exception caught\n{e.Message}", null);
            }
        }
        #endregion
    }
}
