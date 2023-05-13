using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public enum RedoType {
        YesAll = 0,
        YesThreshold = 1,
        No = 2
    }

    [MessagePack.MessagePackObject]
    public class RedoLeggacyOption {
        [MessagePack.Key(0)]
        public int numVal { get; set; }
        [MessagePack.Key(1)]
        public string menuText { get; set; }
        [MessagePack.Key(2)]
        public RedoType type { get; set; }

        public RedoLeggacyOption(int numVal) {
            this.numVal = numVal;
            switch(numVal) {
                case 0:
                    type = RedoType.YesAll;
                    menuText = "Yes (Will redo all contracts to help out others)";
                    break;
                case 1:
                    type = RedoType.YesThreshold;
                    menuText = "Yes (If previous score was under [X] score)";
                    break;
                case 2:
                default:
                    type = RedoType.No;
                    menuText = "No (Will still be assigned to incomplete leggacies)";
                    break;
            }
        }
    }
}
