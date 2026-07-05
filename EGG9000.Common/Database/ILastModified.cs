using System;

namespace EGG9000.Common.Database {
    public interface ILastModified {
        DateTimeOffset LastModified { get; set; }
    }
}
