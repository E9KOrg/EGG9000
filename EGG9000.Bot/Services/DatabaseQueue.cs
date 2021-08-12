using EGG9000.Common.Database;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    public class DatabaseChange {
        public object Item { get; set; }
        public string Property { get; set; }
        public object Value { get; set; }
    }

    public class DatabaseQueue : BackgroundService {
        private ApplicationDbContext _db { get; set; }
        private ConcurrentQueue<DatabaseChange> _queue { get; set; }

        public DatabaseQueue(ApplicationDbContext db) => _db = db; 

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            DatabaseChange item;
            _queue = new ConcurrentQueue<DatabaseChange>();
            while(!stoppingToken.IsCancellationRequested) {
                while(_queue.TryDequeue(out item)) {
                    _db.Attach(item.Item);
                    item.Item.GetType().GetProperty(item.Property).SetValue(item.Item, item.Value, null);
                    //await _db.SaveChangesAsync();

                }

                var changes = _db.ChangeTracker.Entries().Where(x => x.State == Microsoft.EntityFrameworkCore.EntityState.Modified).SelectMany(x => x.Properties.Where(y => y.IsModified));
                if(changes.Count() > 0) {

                }
                await Task.Delay(10, stoppingToken);
            }
        }

        public override void Dispose() {
            base.Dispose();
        }

        public void AddChange(DatabaseChange change) {
            _queue.Enqueue(change);
        }
    }
}
