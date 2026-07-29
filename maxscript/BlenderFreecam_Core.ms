/*
    Blender Freecam for 3ds Max 2025
    Directly navigates the active perspective viewport. No scene camera is made.
*/

global BlenderFreecam
global BlenderFreecamRoot
global BlenderFreecamTimerTick

BlenderFreecamRoot = getFilenamePath (getSourceFileName())

fn BlenderFreecamTimerTick sender eventArgs =
(
    if BlenderFreecam != undefined do BlenderFreecam.tick()
)

if BlenderFreecam != undefined do
(
    try (BlenderFreecam.stop()) catch ()
    BlenderFreecam = undefined
)

struct BlenderFreecamController
(
    inputService,
    updateTimer,
    maxCore,
    redrawFlags,
    active = false,
    promptPushed = false,
    viewportID = 0,
    viewportWindow = 0,
    cameraPosition = [0.0, 0.0, 0.0],
    cameraRight = [1.0, 0.0, 0.0],
    cameraUp = [0.0, 0.0, 1.0],
    cameraForward = [0.0, 1.0, 0.0],
    speed = 250.0,
    minSpeed = 0.01,
    maxSpeed = 10000000.0,
    fastMultiplier = 4.0,
    wheelMultiplier = 1.25,
    fovStep = 2.0,
    minFov = 5.0,
    maxFov = 160.0,
    mouseSensitivity = 0.15,
    rollSpeed = 90.0,

    fn clampValue value minimum maximum =
    (
        if value < minimum then minimum
        else if value > maximum then maximum
        else value
    ),

    fn fail message =
    (
        messageBox message title:"Blender Freecam"
        false
    ),

    fn ensureInputService =
    (
        if inputService != undefined then
        (
            true
        )
        else
        (
            local assemblyPath = BlenderFreecamRoot + \
                "bin\\BlenderFreecam.Input.1.1.2.dll"
            if not doesFileExist assemblyPath do
                assemblyPath = BlenderFreecamRoot + \
                    "bin\\BlenderFreecam.Input.dll"
            if not doesFileExist assemblyPath then
            (
                fail ("The input component is missing:\n\n" + assemblyPath + \
                      "\n\nReinstall Blender Freecam or rebuild the package.")
            )
            else
            (
                try
                (
                    dotNet.loadAssembly assemblyPath
                    inputService = dotNetObject "BlenderFreecam.Input.FlyInputService"
                    true
                )
                catch
                (
                    fail ("Could not load the Blender Freecam input component.\n\n" + \
                          (getCurrentException() as string))
                )
            )
        )
    ),

    fn orthonormalizeOrientation =
    (
        cameraForward = normalize cameraForward
        cameraRight = normalize (cross cameraForward cameraUp)
        cameraUp = normalize (cross cameraRight cameraForward)
    ),

    fn initializeOrientation =
    (
        local cameraTM = inverseHighPrecision (viewport.getTM())
        cameraPosition = cameraTM.row4
        cameraRight = normalize cameraTM.row1
        cameraUp = normalize cameraTM.row2
        cameraForward = normalize (-cameraTM.row3)
        orthonormalizeOrientation()
    ),

    fn rotateVectorAroundAxis vector axis angle =
    (
        local unitAxis = normalize axis
        local cosine = cos angle
        local sine = sin angle
        (vector * cosine) + \
            ((cross unitAxis vector) * sine) + \
            (unitAxis * ((dot unitAxis vector) * (1.0 - cosine)))
    ),

    fn initializeInteractiveRedraw =
    (
        try
        (
            local globalInterface = dotNetClass "Autodesk.Max.GlobalInterface"
            redrawFlags = dotNetClass "Autodesk.Max.RedrawFlags"
            maxCore = globalInterface.Instance.COREInterface
            true
        )
        catch
        (
            maxCore = undefined
            redrawFlags = undefined
            false
        )
    ),

    fn beginInteractiveRedraw =
    (
        if maxCore != undefined do
            try (maxCore.RedrawViews maxCore.Time redrawFlags.Begin undefined) catch ()
    ),

    fn requestInteractiveRedraw =
    (
        if maxCore != undefined then
        (
            try
            (
                maxCore.RedrawViews maxCore.Time redrawFlags.Interactive undefined
            )
            catch
            (
                setNeedsRedraw()
            )
        )
        else
        (
            setNeedsRedraw()
        )
    ),

    fn endInteractiveRedraw =
    (
        if maxCore != undefined then
        (
            try
            (
                maxCore.RedrawViews maxCore.Time redrawFlags.End undefined
            )
            catch
            (
                redrawViews()
            )
        )
        else
        (
            redrawViews()
        )
    ),

    fn currentViewportIsCaptured =
    (
        local currentID = 0
        local currentWindow = 0
        try
        (
            currentID = viewport.GetID viewport.activeViewport
            currentWindow = viewport.getHWnd()
        )
        catch
        (
            return false
        )

        currentID == viewportID and currentWindow == viewportWindow and \
            viewport.getType() == #view_persp_user
    ),

    /*
        Keep stop() above updateView(), tick(), and start(). MAXScript resolves
        unqualified struct member calls in declaration order.
    */
    fn stop reason:undefined =
    (
        active = false

        if updateTimer != undefined do
        (
            try (updateTimer.Stop()) catch ()
            try (dotNet.removeAllEventHandlers updateTimer) catch ()
            try (updateTimer.Dispose()) catch ()
            updateTimer = undefined
        )

        if inputService != undefined do
            try (inputService.Stop()) catch ()

        if promptPushed do
        (
            try (popPrompt()) catch ()
            promptPushed = false
        )

        if reason != undefined do
            format "Blender Freecam: %\n" reason

        endInteractiveRedraw()
        true
    ),

    fn updateView inputFrame =
    (
        local yawAngle = -(inputFrame.MouseDeltaX * mouseSensitivity)
        local pitchAngle = -(inputFrame.MouseDeltaY * mouseSensitivity)
        local rollDirection = 0.0
        if inputFrame.RollLeft do rollDirection = rollDirection - 1.0
        if inputFrame.RollRight do rollDirection = rollDirection + 1.0
        local rollAngle = rollDirection * rollSpeed * inputFrame.DeltaSeconds
        local rotated = yawAngle != 0.0 or pitchAngle != 0.0 or rollAngle != 0.0

        if yawAngle != 0.0 do
        (
            cameraRight = rotateVectorAroundAxis cameraRight cameraUp yawAngle
            cameraForward = rotateVectorAroundAxis cameraForward cameraUp yawAngle
        )

        if pitchAngle != 0.0 do
        (
            cameraForward = rotateVectorAroundAxis cameraForward cameraRight pitchAngle
            cameraUp = rotateVectorAroundAxis cameraUp cameraRight pitchAngle
        )

        if rollAngle != 0.0 do
        (
            cameraRight = rotateVectorAroundAxis cameraRight cameraForward rollAngle
            cameraUp = rotateVectorAroundAxis cameraUp cameraForward rollAngle
        )

        if rotated do orthonormalizeOrientation()

        local worldUp = [0.0, 0.0, 1.0]
        local movement = [0.0, 0.0, 0.0]

        if inputFrame.Forward do movement = movement + cameraForward
        if inputFrame.Backward do movement = movement - cameraForward
        if inputFrame.Left do movement = movement - cameraRight
        if inputFrame.Right do movement = movement + cameraRight
        if inputFrame.Up do movement = movement + worldUp
        if inputFrame.Down do movement = movement - worldUp

        local moved = (length movement) > 0.000001
        if moved do
        (
            local frameSpeed = speed
            if inputFrame.Fast do frameSpeed = frameSpeed * fastMultiplier
            movement = normalize movement
            cameraPosition = cameraPosition + \
                (movement * frameSpeed * inputFrame.DeltaSeconds)
        )

        if rotated or moved do
        (
            local cameraTM = matrix3 \
                cameraRight cameraUp (-cameraForward) cameraPosition
            if viewport.setTM (inverseHighPrecision cameraTM) then
            (
                requestInteractiveRedraw()
            )
            else
            (
                stop reason:"3ds Max rejected the viewport transform."
            )
        )
    ),

    fn tick =
    (
        if not active do return false

        try
        (
            local inputFrame = inputService.Poll()

            if inputFrame.EscapePressed or not inputService.Active then
            (
                stop()
                return false
            )

            if not inputFrame.IsContextActive then
            (
                stop reason:"Freecam stopped because 3ds Max lost focus."
                return false
            )

            if not currentViewportIsCaptured() then
            (
                stop reason:"Freecam stopped because the active viewport changed."
                return false
            )

            if inputFrame.WheelSteps != 0.0 do
            (
                speed = clampValue \
                    (speed * (pow wheelMultiplier inputFrame.WheelSteps)) \
                    minSpeed maxSpeed
            )

            if inputFrame.FovWheelSteps != 0.0 do
            (
                local newFov = clampValue \
                    ((viewport.GetFOV()) - \
                        (inputFrame.FovWheelSteps * fovStep)) \
                    minFov maxFov
                if viewport.SetFOV newFov do requestInteractiveRedraw()
            )

            updateView inputFrame
            true
        )
        catch
        (
            local errorText = getCurrentException() as string
            stop reason:("Freecam stopped after an error: " + errorText)
            false
        )
    ),

    fn createTimer =
    (
        updateTimer = dotNetObject "System.Windows.Forms.Timer"
        updateTimer.Interval = 16
        dotNet.addEventHandler updateTimer "Tick" BlenderFreecamTimerTick
        updateTimer.Start()
    ),

    fn start =
    (
        if active do return true
        if not ensureInputService() do return false

        local viewType = viewport.getType()
        if viewType != #view_persp_user and viewType != #view_iso_user do
        (
            return fail \
                "Freecam only works in a Perspective or User viewport.\n\nCamera, light, orthographic, grid, shape, and extended views are excluded."
        )

        try
        (
            /*
                viewport.setTM only accepts Perspective views. Preserve the User
                view transform while promoting that viewport to Perspective.
            */
            if viewType == #view_iso_user do
            (
                local userViewTM = viewport.getTM()
                if not viewport.setType #view_persp_user do
                    throw "Could not convert the User viewport to Perspective."
                if not viewport.setTM userViewTM do
                    throw "Could not preserve the User viewport transform."
            )

            viewportID = viewport.GetID viewport.activeViewport
            viewportWindow = viewport.getHWnd()
            initializeOrientation()
            initializeInteractiveRedraw()

            local began = inputService.Begin \
                (viewportWindow as integer64) \
                ((windows.getMAXHWND()) as integer64)

            if not began do
                throw ("Input capture could not start. " + inputService.LastError)

            active = true
            beginInteractiveRedraw()
            pushPrompt \
                "FREECAM | WASD move | Space/E up | Q down | Ctrl+Q/E roll | Shift fast | Wheel speed | Ctrl+Wheel FOV | Esc exit"
            promptPushed = true
            createTimer()
            true
        )
        catch
        (
            local errorText = getCurrentException() as string
            stop()
            fail ("Could not start freecam.\n\n" + errorText)
        )
    ),

    fn toggle =
    (
        if active then stop() else start()
    )
)

BlenderFreecam = BlenderFreecamController()

/*
    Lifecycle cleanup is deliberately redundant with the timer checks: it makes
    sure a scene reset/open or application shutdown cannot leave Win32 hooks or
    cursor confinement behind.
*/
try (callbacks.removeScripts id:#BlenderFreecamLifecycle) catch ()
try
(
    callbacks.addScript #systemPreReset \
        "try (BlenderFreecam.stop()) catch ()" \
        id:#BlenderFreecamLifecycle
) catch ()
try
(
    callbacks.addScript #systemPreNew \
        "try (BlenderFreecam.stop()) catch ()" \
        id:#BlenderFreecamLifecycle
) catch ()
try
(
    callbacks.addScript #filePreOpen \
        "try (BlenderFreecam.stop()) catch ()" \
        id:#BlenderFreecamLifecycle
) catch ()
try
(
    callbacks.addScript #systemShutdown \
        "try (BlenderFreecam.stop()) catch ()" \
        id:#BlenderFreecamLifecycle
) catch ()
