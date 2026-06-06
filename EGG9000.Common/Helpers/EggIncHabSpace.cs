using EGG9000.Common.Database;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    public class EggIncHabSpace {
        public class Hab {
            public int id { get; set; }
            public string name { get; set; }
            public string iconPath { get; set; }
            public double baseHabSpace { get; set; }
        }

        private static List<Hab> _habs { get; set; }

        public static double GetBaseHabSpace(CustomFarm farm) {
            double baseSpace = 0;
            if(farm?.Habs == null)
                return baseSpace;
            foreach(var habId in farm.Habs) {
                var hab = Habs.FirstOrDefault(x => x.id == habId);
                if(hab != null) {
                    baseSpace += hab.baseHabSpace;
                }
            }

            return baseSpace;
        }

        public static List<Hab> Habs = new List<Hab> {
          new Hab{
            id = 0,
            name = "Coop",
            iconPath = "egginc/ei_hab_icon_coop.png",
            baseHabSpace = 250,
          },
          new Hab {
            id = 1,
            name = "Shack",
            iconPath = "egginc/ei_hab_icon_shack.png",
            baseHabSpace = 500,
          },
          new Hab {
            id = 2,
            name = "Super Shack",
            iconPath = "egginc/ei_hab_icon_super_shack.png",
            baseHabSpace = 1e3,
          },
          new Hab {
            id = 3,
            name = "Short House",
            iconPath = "egginc/ei_hab_icon_short_house.png",
            baseHabSpace = 2e3,
          },
          new Hab {
            id = 4,
            name = "The Standard",
            iconPath = "egginc/ei_hab_icon_the_standard.png",
            baseHabSpace = 5e3,
          },
          new Hab {
            id = 5,
            name = "Long House",
            iconPath = "egginc/ei_hab_icon_long_house.png",
            baseHabSpace = 1e4,
          },
          new Hab {
            id = 6,
            name = "Double Decker",
            iconPath = "egginc/ei_hab_icon_double_decker.png",
            baseHabSpace = 2e4,
          },
          new Hab {
            id = 7,
            name = "Warehouse",
            iconPath = "egginc/ei_hab_icon_warehouse.png",
            baseHabSpace = 5e4,
          },
          new Hab {
            id = 8,
            name = "Center",
            iconPath = "egginc/ei_hab_icon_center.png",
            baseHabSpace = 1e5,
          },
          new Hab {
            id = 9,
            name = "Bunker",
            iconPath = "egginc/ei_hab_icon_bunker.png",
            baseHabSpace = 2e5,
          },
          new Hab {
            id = 10,
            name = "Eggkea",
            iconPath = "egginc/ei_hab_icon_eggkea.png",
            baseHabSpace = 5e5,
          },
          new Hab {
            id = 11,
            name = "HAB 1000",
            iconPath = "egginc/ei_hab_icon_hab1k.png",
            baseHabSpace = 1e6,
          },
          new Hab {
            id = 12,
            name = "Hangar",
            iconPath = "egginc/ei_hab_icon_hanger.png",
            baseHabSpace = 2e6,
          },
          new Hab {
            id = 13,
            name = "Tower",
            iconPath = "egginc/ei_hab_icon_tower.png",
            baseHabSpace = 5e6,
          },
          new Hab {
            id = 14,
            name = "HAB 10,000",
            iconPath = "egginc/ei_hab_icon_hab10k.png",
            baseHabSpace = 1e7,
          },
          new Hab {
            id = 15,
            name = "Eggtopia",
            iconPath = "egginc/ei_hab_icon_eggtopia.png",
            baseHabSpace = 2.5e7,
          },
          new Hab {
            id = 16,
            name = "Monolith",
            iconPath = "egginc/ei_hab_icon_monolith.png",
            baseHabSpace = 5e7,
          },
          new Hab {
            id = 17,
            name = "Planet Portal",
            iconPath = "egginc/ei_hab_icon_portal.png",
            baseHabSpace = 1e8,
          },
          new Hab {
            id = 18,
            name = "Chicken Universe",
            iconPath = "egginc/ei_hab_icon_chicken_universe.png",
            baseHabSpace = 6e8,
          },
        };
    }
}
