using Ei;
using MassTransit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static Ei.Backup.Types;

namespace EGG9000.Common.Helpers {
    public class ArtifactHelpers {

        public static string GetArtifactFairnessScoreString(List<ArtifactCount> ArtifactHall) {
            if(ArtifactHall is null || ArtifactHall.Count == 0) return "0 (null artifact hall)";
            else return GetArtifactFairnessScore(ArtifactHall).ToString("E");
        }

        public static BigInteger GetArtifactFairnessScore(List<ArtifactCount> ArtifactHall) {
            if(ArtifactHall is null || ArtifactHall.Count == 0) return 0;
            else {
                BigInteger score = 0;
                foreach(var aCount in ArtifactHall) {
                    score += (BigInteger)(Math.Pow(GetFairness(aCount.Artifact)[aCount.Artifact.Tier - 1], aCount.Artifact.Rarity + 1) * aCount.Count);
                }
                return score;
            }
        }

        public static long[] GetFairness(EggIncArtifactInstance instance) {
            if(instance is null || instance.Artifact is null || instance.Tier < 0) return "";
            return instance.Artifact switch {
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
            if(instance is null) return "";
            return instance.Rarity switch {
                1 => "",
                2 => "<:Rare:905959988030226453>",
                3 => "<:Epic:905960149720649748>",
                4 => "<:Legendary:905960165860339722>",
                _ => ""
            };
        }

        public static string GetAfEmoji(EggIncArtifactInstance instance) {
            if(instance is null || instance.Artifact is null || instance.Tier < 0) return "";
            return instance.Artifact switch {
                "Aurelian Brooch" => new string[] { "<:Afx_Aurelian_Brooch_1:801924296213659720>", "<:Afx_Aurelian_Brooch_2:801924317109288981>", "<:Afx_Aurelian_Brooch_3:801924329759571992>", "<:Afx_Aurelian_Brooch_4:801400352338739210>" }[instance.Tier - 1],
                "Beak of Midas" => new string[] { "<:Afx_Beak_Of_Midas_1:801923763141738527>", "<:Afx_Beak_Of_Midas_2:801923773669441546>", "<:Afx_Beak_Of_Midas_3:801923785963601941>", "<:Afx_Beak_Of_Midas_4:801400369145970688>" }[instance.Tier - 1],
                "Book of Basan" => new string[] { "<:Afx_Book_Of_Basan_1:801924039102562375>", "<:Afx_Book_Of_Basan_2:801924052922925096>", "<:Afx_Book_Of_Basan_3:801924064180174931>", "<:Afx_Book_Of_Basan_4:801400385230733333>" }[instance.Tier - 1],
                "Carved Rainstick" => new string[] { "<:Afx_Carved_Rainstick_1:801940593416077353>", "<:Afx_Carved_Rainstick_2:801940608453050438>", "<:Afx_Carved_Rainstick_3:801940622927462480>", "<:Afx_Carved_Rainstick_4:801400417979990066>" }[instance.Tier - 1],
                "Clarity Stone" => new string[] { "<:Afx_Clarity_Stone_1:807798847073550366>", "<:Afx_Clarity_Stone_2:807798825163030528>", "<:Afx_Clarity_Stone_3:807798807852613692>", "<:Afx_Clarity_Stone_4:801400496946282526>" }[instance.Tier],
                "Demeters Necklace" => new string[] { "<:Afx_Demeters_Necklace_1:801791558407159829>", "<:Afx_Demeters_Necklace_2:801791575348609034>", "<:Afx_Demeters_Necklace_3:801791588988092446>", "<:Afx_Demeters_Necklace_4:801400518710788096>" }[instance.Tier - 1],
                "Dilithium Monocle" => new string[] { "<:Afx_Dilithium_Monocle_1:801786173390979122>", "<:Afx_Dilithium_Monocle_2:801786191748923403>", "<:Afx_Dilithium_Monocle_3:801786210266775562>", "<:Afx_Dilithium_Monocle_4:801402979470278656>" }[instance.Tier - 1],
                "Dilithium Stone" => new string[] { "<:Afx_Dilithium_Stone_1:807798908897591296>", "<:Afx_Dilithium_Stone_2:807798889901588490>", "<:Afx_Dilithium_Stone_3:807798875153891368>", "<:Afx_Dilithium_Stone_4:801400591363342380>" }[instance.Tier],
                "Gusset" => new string[] { "<:Afx_Ornate_Gusset_1:801790964679180339>", "<:Afx_Ornate_Gusset_2:801790982563561502>", "<:Afx_Ornate_Gusset_3:801790999697293342>", "<:Afx_Ornate_Gusset_4:801400934236160011>" }[instance.Tier - 1],
                "Interstellar Compass" => new string[] { "<:Afx_Interstellar_Compass_1:801946444600311818>", "<:Afx_Interstellar_Compass_2:801946468734861322>", "<:Afx_Interstellar_Compass_3:801946486627106817>", "<:Afx_Interstellar_Compass_4:801402991776104478>" }[instance.Tier - 1],
                "Life Stone" => new string[] { "<:Afx_Life_Stone_1:807798970792935426>", "<:Afx_Life_Stone_2:807798955810488321>", "<:Afx_Life_Stone_3:807798940539944990>", "<:Afx_Life_Stone_4:801400782557151252>" }[instance.Tier],
                "Light of Eggendil" => new string[] { "<:Afx_Light_Of_Eggendil_1:801948939296702476>", "<:Afx_Light_Of_Eggendil_2:801790900110884874>", "<:Afx_Light_Of_Eggendil_3:801790909250404382>", "<:Afx_Light_Of_Eggendil_4:801400809551560734>" }[instance.Tier - 1],
                "Lunar Stone" => new string[] { "<:Afx_Lunar_Stone_1:807799032306860062>", "<:Afx_Lunar_Stone_2:807799013062606850>", "<:Afx_Lunar_Stone_3:807798994167922690>", "<:Afx_Lunar_Stone_4:801400832640679946>" }[instance.Tier],
                "Lunar Totem" => new string[] { "<:Afx_Lunar_Totem_1:801923971880321085>", "<:Afx_Lunar_Totem_2:801923990872522752>", "<:Afx_Lunar_Totem_3:801924010203938817>", "<:Afx_Lunar_Totem_4:801400854037004289>" }[instance.Tier - 1],
                "Mercury's Lens" => new string[] { "<:Afx_Mercurys_Lens_1:801950726909722665>", "<:Afx_Mercurys_Lens_2:801950753585365034>", "<:Afx_Mercurys_Lens_3:801403003185266708>", "<:Afx_Mercurys_Lens_4:801400885149564928>" }[instance.Tier - 1],
                "Neodymium Medallion" => new string[] { "<:Afx_Neo_Medallion_1:801923701162901504>", "<:Afx_Neo_Medallion_2:801923722973413467>", "<:Afx_Neo_Medallion_3:801923743894863872>", "<:Afx_Neo_Medallion_4:801400910060060673>" }[instance.Tier - 1],
                "Phoenix Feather" => new string[] { "<:Afx_Phoenix_Feather_1:801924123302428702>", "<:Afx_Phoenix_Feather_2:801924144534519818>", "<:Afx_Phoenix_Feather_3:801924163022749736>", "<:Afx_Phoenix_Feather_4:801403016195473418>" }[instance.Tier - 1],
                "Prophecy Stone" => new string[] { "<:Afx_Prophecy_Stone_1:807799478722887680>", "<:Afx_Prophecy_Stone_2:807799450608205905>", "<:Afx_Prophecy_Stone_3:807799057384341521>", "<:Afx_Prophecy_Stone_4:801400987809873971>" }[instance.Tier],
                "Puzzle Cube" => new string[] { "<:Afx_Puzzle_Cube_1:801954137486655589>", "<:Afx_Puzzle_Cube_2:801954163926761522>", "<:Afx_Puzzle_Cube_3:801954186517413918>", "<:Afx_Puzzle_Cube_4:801401010718638080>" }[instance.Tier - 1],
                "Quantum Metronome" => new string[] { "<:Afx_Quantum_Metronome_1:801923609483804694><:Afx_Quantum_Metronome_2:801923633424760863><:Afx_Quantum_Metronome_3:801923659220123698><:Afx_Quantum_Metronome_4:801401052720922634>" }[instance.Tier - 1],
                "Quantum Stone" => new string[] { "<:Afx_Quantum_Stone_1:807799549954883624>", "<:Afx_Quantum_Stone_2:807799527498711040>", "<:Afx_Quantum_Stone_3:807799509630582854>", "<:Afx_Quantum_Stone_4:801401076712079381>" }[instance.Tier],
                "Shell Stone" => new string[] { "<:Afx_Shell_Stone_1:807799916881117214>", "<:Afx_Shell_Stone_2:807799894089007145>", "<:Afx_Shell_Stone_3:807799866142490644>", "<:Afx_Shell_Stone_4:801401100573343776>" }[instance.Tier],
                "Ship in a Bottle" => new string[] { "<:Afx_Ship_In_A_Bottle_1:801955542238363688>", "<:Afx_Ship_In_A_Bottle_2:801955569858641981>", "<:Afx_Ship_In_A_Bottle_3:801955999123636244>", "<:Afx_Ship_In_A_Bottle_4:801746240785481740>" }[instance.Tier - 1],
                "Soul Stone" => new string[] { "<:Afx_Soul_Stone_1:807800048662085663>", "<:Afx_Soul_Stone_2:807800028634415144>", "<:Afx_Soul_Stone_3:807799997764075521>", "<:Afx_Soul_Stone_4:801401175487807519>" }[instance.Tier],
                "Tachyon Deflector" => new string[] { "<:Afx_Tachyon_Deflector_1:801956755655229510>", "<:Afx_Tachyon_Deflector_2:801956779272437800>", "<:Afx_Tachyon_Deflector_3:801956823296770071>", "<:Afx_Tachyon_Deflector_4:801401224292728842>" }[instance.Tier - 1],
                "Tachyon Stone" => new string[] { "<:Afx_Tachyon_Stone_1:807800396264767528>", "<:Afx_Tachyon_Stone_2:807800375100964864>", "<:Afx_Tachyon_Stone_3:807800354380840980>", "<:Afx_Tachyon_Stone_4:801401251278880788>" }[instance.Tier],
                "Terra Stone" => new string[] { "<:Afx_Terra_Stone_1:807800715929583626>", "<:Afx_Terra_Stone_2:807800439831658506>", "<:Afx_Terra_Stone_3:807800418549366826>", "<:Afx_Terra_Stone_4:801401301107343390>" }[instance.Tier],
                "The Chalice" => new string[] { "<:Afx_The_Chalice_1:801923476294205480>", "<:Afx_The_Chalice_2:801923503058059287>", "<:Afx_The_Chalice_3:801923523319693342>", "<:Afx_The_Chalice_4:801401326708850698>" }[instance.Tier - 1],
                "Titanium Actuator" => new string[] { "<:Afx_Titanium_Actuator_1:801957617253351435>", "<:Afx_Titanium_Actuator_2:801957652052967474>", "<:Afx_Titanium_Actuator_3:801957676808536094>", "<:Afx_Titanium_Actuator_4:801401362763874304>" }[instance.Tier - 1],
                "Tungsten Ankh" => new string[] { "<:Afx_Tungsten_Ankh_1:801924204605866035>", "<:Afx_Tungsten_Ankh_2:801924230731399178>", "<:Afx_Tungsten_Ankh_3:801924249522012220>", "<:Afx_Tungsten_Ankh_4:801401388256591932>" }[instance.Tier - 1],
                "Vial of Martian Dust" => new string[] { "<:Afx_Vial_Martian_Dust_1:801923830023979008>", "<:Afx_Vial_Martian_Dust_2:801923862836412427>", "<:Afx_Vial_Martian_Dust_3:801923891534364672>", "<:Afx_Vial_Martian_Dust_4:801401410578939914>" }[instance.Tier - 1],
                _ => instance.Artifact.ToString()
            };
        }

        public static uint GetTotalCraftWithLegendaryPossibility(List<ArtifactCount> artifactHall) {
            if(artifactHall is null || artifactHall.Count == 0) return 0;

            uint totalCrafts = 0;

            foreach(var artifactCount in artifactHall) {
                var artifactName = artifactCount.Artifact.Artifact;

                if(artifactCount.Artifact.Tier == 4 && artifactName != "Lunar Totem") {
                    totalCrafts += artifactCount.NumberCrafted;
                } else if(artifactCount.Artifact.Tier == 3 && artifactName == "Tungsten Ankh") {
                    totalCrafts += artifactCount.NumberCrafted;
                }
            }
            return totalCrafts;
        }

        public static int GetLegendaryArtifactCount(List<ArtifactCount> artifactHall) {
            int legendaryCount = 0;

            foreach (var artifactCount in artifactHall) {
                if(artifactCount.Artifact.Rarity == 4) {
                    legendaryCount += artifactCount.Count;
                }
            }
            return legendaryCount;
        }
    }
}
