namespace EGG9000.Common.Extensions {
    public static class UInt32Extensions {
        public static string Format(this uint num) {
            if(num == 0) {
                return "0";
            }
            return num switch {
                >= 100000000 => (num / 1000000D).ToString("0.#M"),
                >= 1000000 => (num / 1000000D).ToString("0.#M"),
                >= 100000 => (num / 1000D).ToString("0.#K"),
                >= 10000 => (num / 1000D).ToString("0.#K"),
                _ => num.ToString("#,0")
            };
        }
    }
}