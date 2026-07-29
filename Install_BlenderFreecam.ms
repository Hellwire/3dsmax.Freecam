/*
    Run this script once from 3ds Max:
    Scripting > Run Script > Install_BlenderFreecam.ms
*/

global BlenderFreecam

(
    local sourceRoot = getFilenamePath (getSourceFileName())
    local sourceCore = sourceRoot + "maxscript\\BlenderFreecam_Core.ms"
    local sourceMacro = sourceRoot + "maxscript\\BlenderFreecam.mcr"
    local sourceStartup = sourceRoot + "maxscript\\BlenderFreecam_Startup.ms"
    local sourceAssembly = sourceRoot + \
        "bin\\BlenderFreecam.Input.1.1.2.dll"

    local installRoot = (getDir #userScripts) + "\\BlenderFreecam"
    local installBin = installRoot + "\\bin"
    local installCore = installRoot + "\\BlenderFreecam_Core.ms"
    local installMacro = (getDir #userMacros) + "\\BlenderFreecam.mcr"
    local installStartup = (getDir #userStartupScripts) + \
        "\\BlenderFreecam_Startup.ms"
    local installAssembly = installBin + \
        "\\BlenderFreecam.Input.1.1.2.dll"

    fn requireFile path =
    (
        if not doesFileExist path do
            throw ("Required package file is missing:\n" + path)
    )

    fn replaceFile source destination =
    (
        if doesFileExist destination do
        (
            if not deleteFile destination do
                throw ("Could not replace this file. Close and restart 3ds Max, then run the installer again:\n" + destination)
        )

        if not copyFile source destination do
            throw ("Could not copy:\n" + source + "\n\nto:\n" + destination)
    )

    try
    (
        requireFile sourceCore
        requireFile sourceMacro
        requireFile sourceStartup
        requireFile sourceAssembly

        if BlenderFreecam != undefined do
            try (BlenderFreecam.stop()) catch ()

        makeDir installRoot all:true
        makeDir installBin all:true

        replaceFile sourceCore installCore
        replaceFile sourceMacro installMacro
        replaceFile sourceStartup installStartup
        replaceFile sourceAssembly installAssembly

        /*
            Load the new core explicitly so a versioned input assembly can
            upgrade the current 3ds Max session without waiting for restart.
            The Startup copy handles every later launch.
        */
        fileIn installCore
        fileIn installMacro

        messageBox \
            ("Blender Freecam was installed.\n\n" + \
             "Add its button or shortcut from:\n" + \
             "Customize > Customize User Interface\n" + \
             "Category: Blender Freecam\n" + \
             "Action: Freecam\n\n" + \
             "Restart 3ds Max if the action does not immediately appear.") \
            title:"Blender Freecam"
        true
    )
    catch
    (
        messageBox \
            ("Installation failed.\n\n" + (getCurrentException() as string)) \
            title:"Blender Freecam"
        false
    )
)
