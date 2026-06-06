using System;

namespace EGG9000.Bot.Services {
    public class CoopsBeingCreatedService {
        private DateTimeOffset? _coopsAreBeingCreated = null;
        private DateTimeOffset? _coopThreadsAreBeingCreated = null;

        public void SetCoopsAreBeingCreated(bool beingCreated) {
            if(beingCreated) {
                _coopsAreBeingCreated = DateTimeOffset.Now;
            } else {
                _coopsAreBeingCreated = null;
            }
        }

        public void SetCoopThreadsAreBeingCreated(bool beingCreated) {
            if(beingCreated) {
                _coopThreadsAreBeingCreated = DateTimeOffset.Now;
            } else {
                _coopThreadsAreBeingCreated = null;
            }
        }

        public bool AreCoopsBeingCreated() {
            if(_coopsAreBeingCreated.HasValue && _coopsAreBeingCreated.Value.AddMinutes(1) < DateTimeOffset.Now) {
                _coopsAreBeingCreated = null;
            }
            if(_coopThreadsAreBeingCreated.HasValue && _coopThreadsAreBeingCreated.Value.AddMinutes(1) < DateTimeOffset.Now) {
                _coopThreadsAreBeingCreated = null;
            }
            if(_coopsAreBeingCreated is null && _coopThreadsAreBeingCreated is null)
                return false;
            return true;
        }
    }
}
