namespace EGG9000.Common.Helpers {
    // Implemented by the image-render config objects posted to the Site's generate endpoints.
    // Centralizes the "is this config safe to render with" check so controllers can validate
    // uniformly instead of repeating ad-hoc field checks (and missing some, causing 500s).
    public interface IRenderConfig {
        bool IsValid(out string error);
    }
}
