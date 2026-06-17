namespace EGG9000.Common.Database.Entities {
    public class UserSeasonProgress {
        public string EggIncId { get; set; } = string.Empty;
        public string SeasonId { get; set; } = string.Empty;
        public double TotalCxp { get; set; }
        /// <summary>
        /// Stored as (int)Ei.Contract.Types.PlayerGrade — the grade the player was in when this season started.
        /// </summary>
        public int StartingGrade { get; set; }
    }
}
