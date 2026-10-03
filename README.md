![Andromeda banner](Assets/banner.png)

Andromeda.Mod is the runtime gameplay mod for Enemy On Board.

This mod switches from Windwalk servers to the Andromeda servers, as well as multiple other patches that are necessary to make the game work or generally a better experience.

CI/CD secrets:
- ANDROMEDA_GAME_MANAGED_ZIP_URL: required URL to a zip containing Enemy On Board managed DLLs, including Assembly-CSharp.dll (CI generates its publicized reference), or a pre-publicized Assembly-CSharp-Publicized.dll.
- ANDROMEDA_GAME_MANAGED_ZIP_TOKEN: optional bearer token used to access the private URL.

Discord: https://discord.gg/fMbrCUKHP8

Release publishing requires a fresh build from the matching tag. The release workflow checks that the tag, `BuildInfo.Version`, compiled assembly version, and Windows file version agree before uploading the DLL. Manual runs require an existing release tag. Dependency downloads retry transient failures; if the dependency URL remains unavailable, restore `ANDROMEDA_GAME_MANAGED_ZIP_URL` before rerunning. Do not relabel or patch an older DLL to create a new release.
