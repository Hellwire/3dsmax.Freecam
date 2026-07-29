/*
    Explicit per-user startup loader.
    Installed to getDir #userStartupScripts by Install_BlenderFreecam.ms.
*/

global BlenderFreecam

(
    local corePath = (getDir #userScripts) + \
        "\\BlenderFreecam\\BlenderFreecam_Core.ms"
    local macroPath = (getDir #userMacros) + "\\BlenderFreecam.mcr"

    if BlenderFreecam == undefined and doesFileExist corePath do
        fileIn corePath

    /*
        Loading the macro explicitly makes registration deterministic even when
        the user-macro discovery cache is not rebuilt during 3ds Max startup.
    */
    if doesFileExist macroPath do
        fileIn macroPath
)
