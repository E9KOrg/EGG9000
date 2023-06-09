using EGG9000.Common.Database;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace EGG9000.Common.Helpers
{
    public class EpicResearchCalc
    {
        public static List<EpicResearchDetail> GetEpicResearchConfig()
        {
            return JsonConvert.DeserializeObject<List<EpicResearchDetail>>(
                @"
[
	{
        ""order"": 1,
		""id"": ""hold_to_hatch"",
		""costs"": [10, 45, 80, 115, 150, 185, 220, 255, 290, 325, 360, 395, 430, 465, 500],
		""title"": ""Hold to Hatch"",
		""desc"": ""Hold chicken button +2 chickens per second""
	},
	{
        ""order"": 2,
		""id"": ""epic_hatchery"",
		""costs"": [2, 54, 107, 159, 212, 264, 317, 369, 422, 474, 527, 579, 632, 684, 737, 789, 842, 894, 947, 1000],
		""title"": ""Epic Hatchery"",
		""desc"": ""Increase hatchery refill rate by 10%""
	},
	{
        ""order"": 3,
		""id"": ""silo_capacity"",
		""costs"": [5, 110, 215, 320, 425, 530, 635, 740, 845, 950, 1055, 1160, 1265, 1370, 1475, 1580, 1685, 1790, 1895, 2000],
		""title"": ""Silo Capacity"",
		""desc"": ""Increase away time per silo by 6 min""
	},
	{
        ""order"": 4,
		""id"": ""accounting_tricks"",
		""costs"": [100, 621, 1142, 1663, 2184, 2705, 3226, 3747, 4268, 4789, 5310, 5831, 6352, 6873, 7394, 7915, 8436, 8957, 9478, 10000],
		""title"": ""Accounting Tricks"",
		""desc"": ""Increase farm valuation by 5%""
	},
	{
        ""order"": 5,
		""id"": ""epic_internal_incubators"",
		""costs"": [50, 573, 1097, 1621, 2144, 2668, 3192, 3715, 4239, 4763, 5286, 5810, 6334, 6857, 7381, 7905, 8428, 8952, 9476, 10000],
		""title"": ""Epic Int. Hatcheries"",
		""desc"": ""Increase internal hatchery rate by 5%""
	},
	{
        ""order"": 6,
		""id"": ""cheaper_contractors"",
		""costs"": [50, 488, 927, 1366, 1805, 2244, 2683, 3122, 3561, 4000],
		""title"": ""Cheaper Contractors"",
		""desc"": ""Reduce hen house build costs by 5%""
	},
	{
        ""order"": 7,
		""id"": ""bust_unions"",
		""costs"": [50, 488, 927, 1366, 1805, 2244, 2683, 3122, 3561, 4000],
		""title"": ""Bust Unions"",
		""desc"": ""Reduce vehicle hire costs by 5%""
	},
	{
        ""order"": 8,
		""id"": ""cheaper_research"",
		""costs"": [100, 977, 1855, 2733, 3611, 4488, 5366, 6244, 7122, 8000],
		""title"": ""Lab Upgrade"",
		""desc"": ""Reduce research costs by 5%""
	},
	{
        ""order"": 9,
		""id"": ""epic_clucking"",
		""costs"": [100, 621, 1142, 1663, 2184, 2705, 3226, 3747, 4268, 4789, 5310, 5831, 6352, 6873, 7394, 7915, 8436, 8957, 9478, 10000],
		""title"": ""Epic Clucking"",
		""desc"": ""+0.1% egg value bonus for each running chicken""
	},
	{
        ""order"": 10,
		""id"": ""epic_multiplier"",
		""costs"": [50, 150, 251, 351, 452, 552, 653, 753, 854, 954, 1055, 1155, 1256, 1356, 1457, 1557, 1658, 1758, 1859, 1959, 2060, 2160, 2261, 2361, 2462, 2562, 2663, 2763, 2864, 2964, 3065, 3165, 3266, 3366, 3467, 3567, 3668, 3768, 3869, 3969, 4070, 4170, 4271, 4371, 4472, 4572, 4673, 4773, 4874, 4974, 5075, 5175, 5276, 5376, 5477, 5577, 5678, 5778, 5879, 5979, 6080, 6180, 6281, 6381, 6482, 6582, 6683, 6783, 6884, 6984, 7085, 7185, 7286, 7386, 7487, 7587, 7688, 7788, 7889, 7989, 8090, 8190, 8291, 8391, 8492, 8592, 8693, 8793, 8894, 8994, 9095, 9195, 9296, 9396, 9497, 9597, 9698, 9798, 9899, 10000],
		""title"": ""Epic Multiplier"",
		""desc"": ""Increase max running chicken bonus by 2.0x!""
	},
	{
        ""order"": 11,
		""id"": ""drone_rewards"",
		""costs"": [50, 152, 255, 357, 460, 563, 665, 768, 871, 973, 1076, 1178, 1281, 1384, 1486, 1589, 1692, 1794, 1897, 2000],
		""title"": ""Drone Rewards"",
		""desc"": ""Increase your chances for bigger drone rewards by 10%""
	},
	{
        ""order"": 12,
		""id"": ""video_doubler_time"",
		""costs"": [100, 545, 990, 1436, 1881, 2327, 2772, 3218, 3663, 4109, 4554, 5000],
		""title"": ""Video Doubler Time"",
		""desc"": ""Increase video doubler time by 30 min.""
	},
	{
        ""order"": 13,
		""id"": ""int_hatch_calm"",
		""costs"": [250, 763, 1276, 1789, 2302, 2815, 3328, 3842, 4355, 4868, 5381, 5894, 6407, 6921, 7434, 7947, 8460, 8973, 9486, 10000],
		""title"": ""Internal Hatchery Calm"",
		""desc"": ""Increase internal hatchery rate by 10% while away""
	},
	{
        ""order"": 14,
		""id"": ""soul_eggs"",
		""costs"": [500, 532, 564, 597, 629, 661, 694, 726, 758, 791, 823, 856, 888, 920, 953, 985, 1017, 1050, 1082, 1115, 1147, 1179, 1212, 1244, 1276, 1309, 1341, 1374, 1406, 1438, 1471, 1503, 1535, 1568, 1600, 1633, 1665, 1697, 1730, 1762, 1794, 1827, 1859, 1892, 1924, 1956, 1989, 2021, 2053, 2086, 2118, 2151, 2183, 2215, 2248, 2280, 2312, 2345, 2377, 2410, 2442, 2474, 2507, 2539, 2571, 2604, 2636, 2669, 2701, 2733, 2766, 2798, 2830, 2863, 2895, 2928, 2960, 2992, 3025, 3057, 3089, 3122, 3154, 3187, 3219, 3251, 3284, 3316, 3348, 3381, 3413, 3446, 3478, 3510, 3543, 3575, 3607, 3640, 3672, 3705, 3737, 3769, 3802, 3834, 3866, 3899, 3931, 3964, 3996, 4028, 4061, 4093, 4125, 4158, 4190, 4223, 4255, 4287, 4320, 4352, 4384, 4417, 4449, 4482, 4514, 4546, 4579, 4611, 4643, 4676, 4708, 4741, 4773, 4805, 4838, 4870, 4902, 4935, 4967, 5000],
		""title"": ""Soul Food"",
		""desc"": ""Increase bonus per soul egg by +1%""
	},
	{
        ""order"": 15,
		""id"": ""prestige_bonus"",
		""costs"": [2000, 2947, 3894, 4842, 5789, 6736, 7684, 8631, 9578, 10526, 11473, 12421, 13368, 14315, 15263, 16210, 17157, 18105, 19052, 20000],
		""title"": ""Prestige Bonus"",
		""desc"": ""Earn +10% soul eggs when you prestige""
	},
	{
        ""order"": 16,
		""id"": ""epic_egg_laying"",
		""costs"": [250, 2868, 5486, 8105, 10723, 13342, 15960, 18578, 21197, 23815, 26434, 29052, 31671, 34289, 36907, 39526, 42144, 44763, 47381, 50000],
		""title"": ""Epic Comfy Nests"",
		""desc"": ""Increase egg laying rate by 5%""
	},
	{
        ""order"": 17,
		""id"": ""transportation_lobbyist"",
		""costs"": [250, 413, 577, 741, 905, 1068, 1232, 1396, 1560, 1724, 1887, 2051, 2215, 2379, 2543, 2706, 2870, 3034, 3198, 3362, 3525, 3689, 3853, 4017, 4181, 4344, 4508, 4672, 4836, 5000],
		""title"": ""Transportation Lobbyists"",
		""desc"": ""Increase vehicle capacity by 5%""
	},
	{
        ""order"": 18,
		""id"": ""int_hatch_sharing"",
		""costs"": [100, 366, 633, 900, 1166, 1433, 1700, 1966, 2233, 2500],
		""title"": ""Internal Hatchery Sharing"",
		""desc"": ""Full habs' internal hatcheries send +10% chicks to other habs""
	},
	{
        ""order"": 19,
		""id"": ""hold_to_research"",
		""costs"": [5000, 5000, 5000, 5000, 5000, 5000, 5000, 5000, 11315, 12105, 12894, 13684, 14473, 15263, 16052, 16842, 17631, 18421, 19210, 20000],
		""title"": ""Hold to Research"",
		""desc"": ""Increase repetition rate when holding down research button by 25%""
	},
	{
        ""order"": 20,
		""id"": ""prophecy_bonus"",
		""costs"": [100000, 325000, 550000, 775000, 1000000],
		""title"": ""Prophecy Bonus"",
		""desc"": ""Increase bonus per egg of prophecy by +1% (compounding)""
	},
	{
        ""order"": 21,
		""id"": ""afx_mission_time"",
		""costs"": [5000, 7435, 9871, 12307, 14743, 17179, 19615, 22051, 24487, 26923, 29358, 31794, 34230, 36666, 39102, 41538, 43974, 46410, 48846, 51282, 53717, 56153, 58589, 61025, 63461, 65897, 68333, 70769, 73205, 75641, 78076, 80512, 82948, 85384, 87820, 90256, 92692, 95128, 97564, 100000],
		""title"": ""FTL Drive Upgrades"",
		""desc"": ""Reduce Artifact mission time of FTL ships by 1%""
	},
	{
        ""order"": 22,
		""id"": ""afx_mission_capacity"",
		""costs"": [50000, 155555, 261111, 366666, 472222, 577777, 683333, 788888, 894444, 1000000],
		""title"": ""Zero-G Quantum Containment"",
		""desc"": ""Increase Artifact mission capacity by 5%""
	}/*,
	{
        ""order"": 23,
		""id"": ""hyperloopStation"",
		""costs"": [50000],
		""title"": ""Hyperloop Train"",
		""desc"": ""Purchasing Hyperloop trains requires you to first construct a hyperloop station""
	},
	{
        ""order"": 24,
		""id"": ""fuelTank"",
		""costs"": [50000, 250000, 1000000, 2000000, 3000000, 4000000, 5000000],
		""title"": ""Fuel Tank"",
		""desc"": ""Increases the capacity of your fuel tank [Pro Permit Only]""
	}*/
]
                 "
                );
        }

        public class EpicResearchDetail
        {
            public int Order { get; set; }
            public string Id { get; set; }
            public int[] Costs { get; set; }
            public string Title { get; set; }
            public string Desc { get; set; }
            public CustomResearch MappedBackupResearch  { get; set; }
        }
    }
}
