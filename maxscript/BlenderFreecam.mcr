/*
    Macro action exposed in Customize User Interface.
    Category: Blender Freecam
*/

global BlenderFreecam

if BlenderFreecam == undefined do
(
    local installedCore = (getDir #userScripts) + \
        "\\BlenderFreecam\\BlenderFreecam_Core.ms"
    local portableCore = (getFilenamePath (getSourceFileName())) + \
        "BlenderFreecam_Core.ms"

    if doesFileExist installedCore then
        fileIn installedCore
    else if doesFileExist portableCore then
        fileIn portableCore
)

macroScript BlenderFreecamToggle
category:"Blender Freecam"
internalCategory:"Blender Freecam"
buttonText:"Freecam"
toolTip:"Toggle Blender-style viewport freecam"
(
    on execute do
    (
        if BlenderFreecam == undefined then
            messageBox \
                "Blender Freecam core could not be loaded. Run Install_BlenderFreecam.ms again." \
                title:"Blender Freecam"
        else
            BlenderFreecam.toggle()
    )

    on isChecked return \
        (BlenderFreecam != undefined and BlenderFreecam.active)
)
