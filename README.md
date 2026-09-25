# studying

i made a small windows app where i can set what im studying and has its own local mini music player. it shows what im studying (task i set on the app) and the song im actively listening to on my discord status.

## the app

![the app ui](preview.png)

## what it looks like on discord

<img width="297" height="367" alt="discord profile status" src="https://github.com/user-attachments/assets/1a146b5f-544c-4b5b-aaf0-02440a476327" />

## using it

- download [studying.exe](studying.exe) and open it on windows. it needs .net framework 4.8.
- put your songs in a `music` folder on your desktop. it plays them in file name order, or you can press shuffle. the volume stays where you set it when the song changes.
- type what youre studying and press **start focus**. discord will show `studying [your task]` and, while a song is playing, `listening to [file name]`. the timer shows how long youve been studying.
- keep the discord desktop app open with activity sharing turned on. the discord status works without setting up an app id. if you want to use your own discord app, right click the **discord activity** bar, choose **discord setup**, and paste your app id. add `studying-photo.png` as a rich presence image named `studying-photo` if you want the same art.
- press the x in the top right to quit.

your songs stay on your pc. the app reads them from `Desktop\music` and saves its settings in `%LOCALAPPDATA%\NowAndDoing\settings.json`.

## building it

on windows, run `powershell -ExecutionPolicy Bypass -File .\build.ps1` from this folder. it puts the new app in `dist\studying.exe`.
