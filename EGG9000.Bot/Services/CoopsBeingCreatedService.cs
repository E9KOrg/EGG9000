using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    public class CoopsBeingCreatedService {
        private DateTimeOffset? _coopsAreBeingCreated = null;
        private DateTimeOffset? _coopThreadsAreBeingCreated = null;

        public void SetCoopsAreBeingCreated(bool beingCreated) {
            if(beingCreated) {
                _coopsAreBeingCreated = DateTimeOffset.UtcNow;
            } else {
                _coopsAreBeingCreated = null;
            }
        }

        public void SetCoopThreadsAreBeingCreated(bool beingCreated) {
            if(beingCreated) {
                _coopThreadsAreBeingCreated = DateTimeOffset.UtcNow;
            } else {
                _coopThreadsAreBeingCreated = null;
            }
        }

        public bool AreCoopsBeingCreated() {
            if(_coopsAreBeingCreated is null)
                return false;
            if(_coopsAreBeingCreated.Value.AddMinutes(1) < DateTimeOffset.UtcNow) {
                _coopsAreBeingCreated = null;
            }
            if(_coopThreadsAreBeingCreated.HasValue && _coopThreadsAreBeingCreated.Value.AddMinutes(1) < DateTimeOffset.UtcNow) {
                _coopThreadsAreBeingCreated = null;
            }
            if(_coopsAreBeingCreated is null && _coopThreadsAreBeingCreated is null)
                return false;
            return true;
        }
    }
}
