using Microsoft.EntityFrameworkCore;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EGG9000.Common.Database.Entities {
    [PrimaryKey(nameof(ID), nameof(Level), nameof(UserId))]
    public class ResearchCostSubmission {
        public string ID { get; set; }
        public int Level { get; set; }
        public double Cost { get; set; }
        public Guid UserId { get; set; }
        public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.Now;
    }

}
