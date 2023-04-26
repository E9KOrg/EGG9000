using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace EGG9000.Common.Database {
    public class ApplicationDbFactory : IDesignTimeDbContextFactory<ApplicationDbContext> {
        public ApplicationDbContext CreateDbContext(string[] args) {
            var Configuration = new ConfigurationBuilder()
                .AddUserSecrets<ApplicationDbFactory>()
                .Build();


            return new ApplicationDbContext(Configuration);
        }
    }
}
