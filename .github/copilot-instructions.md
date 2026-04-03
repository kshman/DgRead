# Copilot Instructions

## 프로젝트 지침
- Use `StorageProvider` for file/folder pickers instead of legacy `OpenFolderDialog`/`OpenFileDialog`, and ensure `async void` UI handlers include explicit exception handling.
- When organizing window code, prefer method order: constructor -> Apply* configuration methods -> window event handlers -> helper methods used by window events -> menu event handlers -> helper methods used by menu events.