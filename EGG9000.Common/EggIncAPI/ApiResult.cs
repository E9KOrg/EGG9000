namespace EGG9000.Common.EggIncAPI {

    // Outcome of an Egg Inc API call: a value, or an error message explaining the failure.
    // Deconstructs to (value, error), so callers that only want the value can write
    // `var (x, _) = await ...` and treat a null x as failure.
    public readonly record struct ApiResult<T>(T Value, string Error) {
        public bool Success => Error is null;
        public bool Failed => Error is not null;

        public static ApiResult<T> Ok(T value) => new(value, null);
        public static ApiResult<T> Fail(string error) => new(default, error);

        public static implicit operator ApiResult<T>(T value) => new(value, null);
    }
}
