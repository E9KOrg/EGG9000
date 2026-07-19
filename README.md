# EGG9000


### Website Test
```
cd EGG9000.Site
dotnet watch --no-hot-reload
```
To bypass discord login for debuggin use `/Home/DebugLogin?id={yourdiscordid}`, this will
only work if you've logged into the dev DB at least once. Ask @Kendrome to spin up a instance.



### Linux Install
**Prerequisites**
```
sudo apt install dotnet-sdk-6.0
```

**Secrets**
Add secrets.json to ~/.microsoft/usersecrets/dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a

**Run**
```
cd EGG9000\EGG9000.Bot
dotnet run --arch x64 --os linux
```
If anybody knows a solution to not need the platform flags let me know.
