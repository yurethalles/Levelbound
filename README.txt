Levelbound - projeto da DLL atual

Conteúdo
- DornsLevelbound/DornsLevelbound.cs: código-fonte da DLL atual.
- ServerSync/: fonte MIT da biblioteca incorporada para sincronização.
- pack-levelbound.proj: etapa que incorpora ServerSync em uma única DLL.

Estado desta versão
Esta é a base atual compilada. Ela contém sincronização, YAML simples, geração
de lista auxiliar, bloqueios de item e feedback visual de tooltip/receita.
Ela NÃO contém ainda o modelo final por bioma definido posteriormente na conversa.

Compilação
O projeto foi compilado contra a instalação local do Valheim e BepInEx 5.
Primeiro compile ServerSync.csproj (Release), depois DornsLevelbound.cs com as
referências do Valheim/BepInEx e, por fim, execute pack-levelbound.proj para
incorporar ServerSync dentro de Levelbound.dll.
