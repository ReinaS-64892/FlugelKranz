ClangSharpPInvokeGenerator \
    --config latest-codegen unix-types \
    --file "./monado/src/xrt/targets/libmonado/monado.h" \
    --include-directory "/usr/lib/clang/22/include" \
    --namespace "MonadoXrApi" \
    --libraryPath "monado" \
    --methodClassName "MonadoXrApiNative" \
    --with-access-specifier "*=internal" \
    --output "./generated/MonadApi.g.cs"
