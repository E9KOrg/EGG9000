using EggSharp.GameData;

using System;
using System.Collections.Generic;

namespace EGG9000.Common.Helpers {
    public static class GameDataSource {
        private static readonly Lazy<GameDataProvider> _provider = new(GameDataProvider.CreateDefault);
        private static readonly Lazy<IReadOnlyDictionary<string, string>> _documents = new(GameDataProvider.EmbeddedDocuments);
        private static readonly Lazy<MissionShipCatalog> _missionShips = new(() => MissionShipCatalog.Parse(_documents.Value[MissionShipCatalog.DocumentId]));
        private static readonly Lazy<ArtifactCatalog> _artifacts = new(() => ArtifactCatalog.Parse(_documents.Value[ArtifactCatalog.DocumentId]));

        public static IGameDataProvider Provider => _provider.Value;
        public static IMissionShipCatalog MissionShips => _missionShips.Value;
        public static IArtifactCatalog Artifacts => _artifacts.Value;
    }
}
