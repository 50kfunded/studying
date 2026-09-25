# studying

i made a small windows app where i can set what im studying and has its own local mini music player. it shows what im studying (task i set on the app) and the song im actively listening to on my discord status.

![the app](<img width="430" height="509" alt="image" src="https://github.com/user-attachments/assets/7c06506c-9db2-47ad-b6e6-17d984380fe7" />
)
what it looks like on discord profile: <img width="297" height="367" alt="image" src="https://github.com/user-attachments/assets/1a146b5f-544c-4b5b-aaf0-02440a476327" />

## using it

- download [studying.exe](studying.exe) and open it on windows. it needs .net framework 4.8.
- put your songs in a `music` folder on your desktop. it plays them in file name order, or you can press shuffle. the volume stays where you set it when the song changes.
- type what youre studying and press **start focus**. discord will show `studying [your task]` and, while a song is playing, `listening to [file name]`. the timer shows how long youve been studying.
- keep the discord desktop app open. right click the **discord activity** bar in the app, choose **discord setup**, and paste your discord application id. if youre using your own discord app, add `studying-photo.png` as a rich presence image named `studying-photo` to show the art.
- press the x in the top right to quit.

your songs stay on your pc. the app reads them from `Desktop\music` and saves its settings in `%LOCALAPPDATA%\NowAndDoing\settings.json`.

## building it

on windows, run `powershell -ExecutionPolicy Bypass -File .\build.ps1` from this folder. it puts the new app in `dist\studying.exe`.
