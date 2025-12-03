using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    public class CoopsBeingCreatedService {
        private DateTimeOffset? _coopsAreBeingCreated = null;

        public void SetCoopsAreBeingCreated(bool beingCreated) {
            if(beingCreated) {
                _coopsAreBeingCreated = DateTimeOffset.Now;
            } else {
                _coopsAreBeingCreated = null;
            }
        }

        public bool AreCoopsBeingCreated() {
            if(_coopsAreBeingCreated is null)
                return false;
            if(_coopsAreBeingCreated.Value.AddMinutes(1) < DateTimeOffset.Now) {
                _coopsAreBeingCreated = null;
                return false;
            }
            return true;
        }
    }
}
