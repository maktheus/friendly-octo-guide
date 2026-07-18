# Shell mobile (Tauri)

Não é um projeto Rust separado: no Tauri v2, mobile e desktop compartilham
**o mesmo crate** — `clients/desktop/src-tauri` (`lib.rs::run()`, já anotado
com `#[cfg_attr(mobile, tauri::mobile_entry_point)]`). Este diretório existe
só pra documentar o que falta pra gerar os projetos nativos; não há nada
pra rodar sem o SDK correto instalado (Android Studio/NDK ou Xcode — nenhum
dos dois existe neste ambiente de deploy, por isso não foi gerado aqui).

## Android

Pré-requisitos: Android Studio + NDK, `ANDROID_HOME`/`NDK_HOME` configurados.

```bash
cd clients/desktop/src-tauri
cargo tauri android init      # gera gen/android/ (projeto Gradle) — uma vez só
PLATAFORMA_GATEWAY_URL=http://<ip-do-gateway>:8180 cargo tauri android dev
cargo tauri android build     # gera o .apk/.aab de release
```

## iOS

Pré-requisitos: macOS + Xcode (não dá pra gerar nem buildar fora do macOS).

```bash
cd clients/desktop/src-tauri
cargo tauri ios init          # gera gen/apple/ (projeto Xcode) — uma vez só
PLATAFORMA_GATEWAY_URL=http://<ip-do-gateway>:8180 cargo tauri ios dev
cargo tauri ios build
```

## Build no Windows (validado em 17/07/2026)

`gen/android` está gerado e commitado; um `app-arm64-debug.apk` já foi produzido
com SDK 36 + NDK 27.2 + Rust host `x86_64-pc-windows-gnu`. Três armadilhas
específicas do Windows, todas contornáveis:

1. **Host GNU precisa do binutils MinGW real no PATH** (`dlltool.exe` + `as.exe`)
   para os crates raw-dylib (windows-sys etc.). O `dlltool` self-contained do
   rustup falha (não acha `as`); o `llvm-dlltool` do NDK aceita os argumentos mas
   gera import libs que **quebram em runtime** (build script morre com
   ACCESS_VIOLATION) — usar o pacote `mingw-w64-x86_64-binutils` do MSYS2.
2. **O symlink do `.so` pra `jniLibs` exige Developer Mode.** Sem ele, o
   fallback é copiar `target/aarch64-linux-android/debug/libplataforma_linha_lib.so`
   pra `gen/android/app/src/main/jniLibs/arm64-v8a/` e rodar
   `./gradlew assembleArm64Debug -x rustBuildArm64Debug` direto no `gen/android`.
3. **Java**: usar o JBR do Android Studio (`JAVA_HOME` → `…\Android Studio\jbr`);
   o Java 8 do PATH do sistema não builda AGP.

## Pendente

- `ios init` (exige macOS + Xcode).
- Assinatura de release (hoje o artefato validado é o APK debug).
- Mobile não tem shell pra `PLATAFORMA_GATEWAY_URL`: precisa de uma tela de
  configuração no primeiro uso (mesmo pendente anotado no README do desktop).
- Push nativo do alerta (ntfy) e desbloqueio via TOTP local — ver arquitetura,
  bloco "06 · Cliente nativo" — ainda não implementados no shell.
