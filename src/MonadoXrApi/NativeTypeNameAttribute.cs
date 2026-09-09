namespace MonadoXrApi;

    internal class NativeTypeNameAttribute(string nativeName) : Attribute
    {        
        public string NativeName { get; } = nativeName;
    }
