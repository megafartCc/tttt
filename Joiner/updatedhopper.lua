-- =========================================================
-- Updated Hopper + Joiner backend reporter
-- Reports: brainrot name + moneyPerSec
-- Hops: local Roblox public server pages + local ignore list
-- =========================================================

local HttpService = game:GetService("HttpService")
local TeleportService = game:GetService("TeleportService")
local CoreGui = game:GetService("CoreGui")
local GuiService = game:GetService("GuiService")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local ReplicatedFirst = game:GetService("ReplicatedFirst")
local MarketplaceService = game:GetService("MarketplaceService")

pcall(function()
    ReplicatedFirst:RemoveDefaultLoadingScreen()
end)

if not game:IsLoaded() then
    game.Loaded:Wait()
end

local LocalPlayer = Players.LocalPlayer or Players.PlayerAdded:Wait()

-- ------------------------------
-- Config
-- ------------------------------
local RemoveErrorPrompts = true
local IterationSpeed = 0.25
local ExcludefullServers = true
local SaveTeleportAttempts = false

local TargetPlaceId = 109983668079237

local JOINER_URL = "https://j-production-875d.up.railway.app"
local JOINER_REPORT_INTERVAL = 10
local JOINER_SERVER_BLOCK_SECONDS = 300
local JOINER_PERMANENT_BLOCK_SENTINEL = -1
local JOINER_TELEPORT_PENDING_TIMEOUT = 0.25
local JOINER_TELEPORT_CONNECT_TIMEOUT_SECONDS = 12
local JOINER_REPORT_ONLY_MODE = false
local JOINER_DEBUG = false
local JOINER_MODULE_RETRY_SECONDS = 5
local JOINER_MPS_INIT_GRACE_SECONDS = 45
local JOINER_MPS_STABILIZE_RETRIES = 3
local JOINER_MPS_STABILIZE_DELAY = 0.35
local RAM_STATUS_PUSH_ENABLED = true
local RAM_STATUS_PUSH_INTERVAL = 1
local RAM_STATUS_PUSH_PORT = 7963
local RAM_STATUS_PUSH_PASSWORD = ""
local RAM_SERVER_CLAIM_ENABLED = true
local RAM_SERVER_CLAIM_LEASE_SECONDS = 300

-- Rejoin handling (for bad target server / unavailable server prompts)
local AUTO_REJOIN_ON_TELEPORT_FAIL = false
local AUTO_REJOIN_ON_ERROR_MESSAGE = false
local AUTO_REJOIN_RETRY_SECONDS = 2
local AUTO_REJOIN_MIN_ATTEMPT_GAP = 1.25
local AUTO_REJOIN_POST_ATTEMPT_CHECK_SECONDS = 8
local AUTO_REJOIN_PROMPT_WATCHDOG = false
local AUTO_REJOIN_PROMPT_SCAN_INTERVAL = 0.2
local AUTO_REJOIN_KICK_IMMEDIATE_DELAY = 0.05
local PROFILE_SESSION_REJOIN_DELAY = 8
local PROFILE_SESSION_REJOIN_JITTER = 2
local SIMPLE_REJOIN_FALLBACK_ENABLED = false
local SIMPLE_REJOIN_MIN_GAP_SECONDS = 2
local SIMPLE_REJOIN_HEARTBEAT_INTERVAL = 0.5
local SIMPLE_REJOIN_HEARTBEAT_TRIGGER = 10
local SIMPLE_REJOIN_ERROR_TTL_SECONDS = 20

-- Proactive no-base watchdog (prevents 267 "No bases available" kicks from stalling).
local NO_BASE_WATCHDOG = false
local NO_BASE_GRACE_SECONDS = 4
local NO_BASE_CHECK_INTERVAL = 0.25
local NO_BASE_MISS_LIMIT = 6
local NO_BASE_FULL_SERVER_HIT_LIMIT = 2

-- Transport stall failsafe (covers 279-style stuck states when prompt hooks miss).
-- If no hop activity happens for STALL_RECOVERY_SECONDS, force auto-rejoin.
local STALL_RECOVERY_ENABLED = true
local STALL_RECOVERY_SECONDS = 30
local STALL_RECOVERY_CHECK_INTERVAL = 0.5
local STALL_RECOVERY_COOLDOWN = 3

-- Keep kick hook disabled for updated hopper (requested).
local ENABLE_KICK_HOOK_REJOIN = false

-- Performance profile for high instance count runs.
local PERFORMANCE_MODE = true
local FPS_CAP = 15
local REMOVE_LOADING_SCREEN = true
local USE_BLANK_TELEPORT_GUI = true
local DISABLE_3D_RENDERING = true
local MUTE_ALL_AUDIO = true
local REMOVE_POST_EFFECTS = true
local REMOVE_TEXTURES = true
local REMOVE_PARTICLES = true

local FILE_SCOPE = tostring(TargetPlaceId) .. "_" .. tostring(LocalPlayer.UserId or "0")
local CACHE_FILE = "Servers_" .. FILE_SCOPE .. ".json"
local ATTEMPTS_FILE = "Attempts_" .. FILE_SCOPE .. ".txt"
local IGNORE_FILE = "ignorelist_" .. FILE_SCOPE .. ".json"
local SHARED_IGNORE_FILE = "ignorelist_shared_" .. tostring(TargetPlaceId) .. ".json"
local ENABLE_SHARED_CROSS_ACCOUNT_BLOCKLIST = true
local SHARED_SERVER_BLOCK_SECONDS = 300
local SHARED_IGNORE_REFRESH_SECONDS = 0.5
local SHARED_SERVER_CLAIM_WAIT_SECONDS = 0.08
local SHARED_SERVER_CLAIM_FILE_PREFIX = "server_claim_" .. tostring(TargetPlaceId) .. "_"

local API = "https://games.roblox.com/v1/games/" .. tostring(TargetPlaceId) .. "/servers/Public?limit=100"

local lastJoinerReportAt = 0
local attemptedServerIds = {}
local sharedAttemptedServerIds = {}
local animalsDataCache = nil
local animalsSharedCache = nil
local plotControllerCache = nil
local animalsDataLastAttemptAt = 0
local animalsSharedLastAttemptAt = 0
local plotControllerLastAttemptAt = 0
local lastGoodMpsByKey = {}
local hadNonZeroMpsReport = false
local joinerShuffleNonce = 0
local lastTeleportTargetJobId = nil
local currentExcludeFullGames = ExcludefullServers
local scriptStartedAt = tick()
local lastSharedIgnoreRefreshAt = 0
local lastHopActionAt = tick()
local lastStallRecoveryAt = 0
local lastRamStatusPushAt = 0
local lastRamStatusPushErrorAt = 0
local cachedPlaceName = ""
local cachedPlaceNamePlaceId = nil
local cachedPlaceNameLookupAt = 0
local function createSharedClaimOwner()
    local userTag = tostring(LocalPlayer.UserId or "0")
    if HttpService and type(HttpService.GenerateGUID) == "function" then
        local okGuid, guid = pcall(HttpService.GenerateGUID, HttpService, false)
        if okGuid and type(guid) == "string" and guid ~= "" then
            return userTag .. "_" .. guid
        end
    end
    return userTag
        .. "_"
        .. tostring(math.floor(os.clock() * 1000000))
        .. "_"
        .. tostring(math.random(100000, 999999))
end
local SHARED_SERVER_CLAIM_OWNER = createSharedClaimOwner()

local function getNowSeconds()
    return os.time()
end

local function joinerDebugWarn(message)
    if JOINER_DEBUG then
        warn("[Joiner Debug] " .. tostring(message))
    end
end

local function runWithElevatedIdentity(callback, ...)
    if type(callback) ~= "function" then
        return false, "invalid_callback"
    end

    local previousIdentity = nil
    if type(getthreadidentity) == "function" and type(setthreadidentity) == "function" then
        local okIdentity, identity = pcall(getthreadidentity)
        if okIdentity then
            previousIdentity = identity
            pcall(setthreadidentity, 8)
        end
    end

    local results = table.pack(pcall(callback, ...))

    if previousIdentity ~= nil and type(setthreadidentity) == "function" then
        pcall(setthreadidentity, previousIdentity)
    end

    return table.unpack(results, 1, results.n)
end

local function getCoreGuiReference()
    local guiRoot = CoreGui
    if type(cloneref) == "function" then
        local okClone, cloned = pcall(cloneref, CoreGui)
        if okClone and typeof(cloned) == "Instance" then
            guiRoot = cloned
        end
    end
    return guiRoot
end

local function triggerButtonSignal(button)
    if typeof(button) ~= "Instance" or not button:IsA("GuiButton") then
        return false
    end

    local fired = false
    pcall(function()
        button:Activate()
        fired = true
    end)

    if type(firesignal) == "function" then
        pcall(function()
            firesignal(button.Activated)
            fired = true
        end)
        pcall(function()
            firesignal(button.MouseButton1Click)
            fired = true
        end)
        pcall(function()
            firesignal(button.MouseButton1Down)
            firesignal(button.MouseButton1Up)
            fired = true
        end)
    end

    if type(getconnections) == "function" then
        for _, signal in ipairs({ button.Activated, button.MouseButton1Click, button.MouseButton1Down, button.MouseButton1Up }) do
            local okConn, conns = pcall(getconnections, signal)
            if okConn and type(conns) == "table" then
                for _, conn in ipairs(conns) do
                    if type(conn) == "table" and type(conn.Fire) == "function" then
                        pcall(function()
                            conn:Fire()
                            fired = true
                        end)
                    end
                end
            end
        end
    end

    pcall(function()
        local vim = game:GetService("VirtualInputManager")
        if vim and button.AbsolutePosition and button.AbsoluteSize then
            local x = math.floor(button.AbsolutePosition.X + (button.AbsoluteSize.X * 0.5))
            local y = math.floor(button.AbsolutePosition.Y + (button.AbsoluteSize.Y * 0.5))
            vim:SendMouseButtonEvent(x, y, 0, true, game, 0)
            vim:SendMouseButtonEvent(x, y, 0, false, game, 0)
            fired = true
        end
    end)

    return fired
end

local function clearRobloxLoadingUi()
    local previousIdentity = nil
    if type(getthreadidentity) == "function" and type(setthreadidentity) == "function" then
        local okIdentity, identity = pcall(getthreadidentity)
        if okIdentity then
            previousIdentity = identity
            pcall(setthreadidentity, 8)
        end
    end

    pcall(function()
        local guiRoot = CoreGui
        if type(cloneref) == "function" then
            local okClone, cloned = pcall(cloneref, CoreGui)
            if okClone and typeof(cloned) == "Instance" then
                guiRoot = cloned
            end
        end

        for _, name in ipairs({ "RobloxLoadingGui", "LoadingGui", "TeleportGui" }) do
            local item = guiRoot:FindFirstChild(name)
            if item then
                item:Destroy()
            end
        end

        local robloxGui = guiRoot:FindFirstChild("RobloxGui")
        if robloxGui then
            local modules = robloxGui:FindFirstChild("Modules")
            if modules then
                local loadingScreen = modules:FindFirstChild("LoadingScreen")
                if loadingScreen then
                    loadingScreen:Destroy()
                end
            end
        end
    end)

    if previousIdentity ~= nil and type(setthreadidentity) == "function" then
        pcall(setthreadidentity, previousIdentity)
    end
end

local function setBlankTeleportGui()
    local teleportGui = Instance.new("ScreenGui")
    teleportGui.Name = "JoinerBlankTeleportGui"
    teleportGui.ResetOnSpawn = false
    teleportGui.IgnoreGuiInset = true

    local fill = Instance.new("Frame")
    fill.Size = UDim2.fromScale(1, 1)
    fill.Position = UDim2.fromScale(0, 0)
    fill.BackgroundColor3 = Color3.new(0, 0, 0)
    fill.BorderSizePixel = 0
    fill.Parent = teleportGui

    pcall(function()
        TeleportService:SetTeleportGui(teleportGui)
    end)
end

local autoRejoinInProgress = false
local autoRejoinLastAttemptAt = 0
local autoRejoinStartedAt = 0
local immediateRejoinBurstActive = false
local profileSessionHoldUntil = 0
local simpleFallbackLastTeleportAt = 0
local simpleFallbackLastErrorAt = 0
local simpleFallbackLastErrorText = ""
local simpleFallbackHeartbeat = 0

local function isProfileSessionErrorMessage(message)
    local text = string.lower(tostring(message or ""))
    if text == "" then
        return false
    end
    return string.find(text, "profile session end", 1, true) ~= nil
        or string.find(text, "please rejoin", 1, true) ~= nil
end

local function markProfileSessionHold(sourceTag)
    local baseDelay = math.max(2, tonumber(PROFILE_SESSION_REJOIN_DELAY) or 8)
    local jitterLimit = math.max(0, tonumber(PROFILE_SESSION_REJOIN_JITTER) or 0)
    local jitter = 0
    if jitterLimit > 0 then
        local seed = math.floor(os.clock() * 1000) + tonumber(LocalPlayer.UserId or 0)
        local rng = Random.new(seed)
        jitter = rng:NextNumber(0, jitterLimit)
    end

    local holdUntil = tick() + baseDelay + jitter
    if holdUntil > profileSessionHoldUntil then
        profileSessionHoldUntil = holdUntil
    end
    joinerDebugWarn("profile-session hold active until=" .. string.format("%.2f", profileSessionHoldUntil) .. " source=" .. tostring(sourceTag))
end

local function startImmediateRejoinBurst(reason)
    if immediateRejoinBurstActive then
        return
    end
    immediateRejoinBurstActive = true

    task.spawn(function()
        local deadline = tick() + 6
        while tick() < deadline do
            clearRobloxLoadingUi()
            if USE_BLANK_TELEPORT_GUI then
                setBlankTeleportGui()
            end
            lastHopActionAt = tick()
            pcall(function()
                TeleportService:Teleport(TargetPlaceId, LocalPlayer)
            end)
            task.wait(0.2)
        end
        immediateRejoinBurstActive = false
        joinerDebugWarn("immediate rejoin burst finished: " .. tostring(reason))
    end)
end

local function queueAutoRejoin(reason, force, immediate, delayOverride)
    local nowTick = tick()
    local minGap = math.max(0.5, tonumber(AUTO_REJOIN_MIN_ATTEMPT_GAP) or 1.25)
    if not force and (nowTick - autoRejoinLastAttemptAt) < minGap then
        return
    end
    if autoRejoinInProgress then
        local age = nowTick - (autoRejoinStartedAt or nowTick)
        if force and age >= 1.5 then
            autoRejoinInProgress = false
            autoRejoinStartedAt = 0
        else
            return
        end
    end

    autoRejoinInProgress = true
    autoRejoinStartedAt = tick()
    autoRejoinLastAttemptAt = nowTick
    task.spawn(function()
        local waitSeconds = math.max(0.25, tonumber(delayOverride) or tonumber(AUTO_REJOIN_RETRY_SECONDS) or 2)
        if immediate and delayOverride == nil then
            waitSeconds = math.max(0, tonumber(AUTO_REJOIN_KICK_IMMEDIATE_DELAY) or 0.05)
        end
        if profileSessionHoldUntil > tick() then
            waitSeconds = math.max(waitSeconds, profileSessionHoldUntil - tick())
        end
        task.wait(waitSeconds)

        clearRobloxLoadingUi()
        if USE_BLANK_TELEPORT_GUI then
            setBlankTeleportGui()
        end

        local originalJobId = tostring(game.JobId or "")
        lastHopActionAt = tick()
        local ok, err = pcall(function()
            TeleportService:Teleport(TargetPlaceId, LocalPlayer)
        end)
        if not ok then
            warn("[Joiner Hopper] Auto-rejoin failed: " .. tostring(err))
            task.delay(math.max(0.5, tonumber(AUTO_REJOIN_RETRY_SECONDS) or 2), function()
                queueAutoRejoin("auto_rejoin_failed:" .. tostring(reason), true, true)
            end)
        else
            joinerDebugWarn("auto-rejoin triggered: " .. tostring(reason))
            task.delay(math.max(3, tonumber(AUTO_REJOIN_POST_ATTEMPT_CHECK_SECONDS) or 8), function()
                if tostring(game.JobId or "") == originalJobId then
                    queueAutoRejoin("post_attempt_still_same_job:" .. tostring(reason), true)
                end
            end)
        end

        autoRejoinInProgress = false
        autoRejoinStartedAt = 0
    end)
end

local function isImmediateRejoinErrorMessage(message)
    local text = string.lower(tostring(message or ""))
    if text == "" then
        return false
    end
    local urgentMarkers = {
        "error code: 267",
        "kicked by this experience",
        "you have been kicked",
        "moderation message",
        "no bases available in this server",
    }
    for _, marker in ipairs(urgentMarkers) do
        if string.find(text, marker, 1, true) then
            return true
        end
    end
    return false
end

local function shouldAutoRejoinForErrorMessage(message)
    local text = string.lower(tostring(message or ""))
    if text == "" then
        return false
    end

    local markers = {
        "teleport failed",
        "failed to teleport",
        "connection failed",
        "failed to connect to the experience",
        "no response from server",
        "server unavailable",
        "server is full",
        "waiting for available server",
        "unable to join",
        "error code: 772",
        "error code: 773",
        "error code: 267",
        "error code: 279",
        "error code: 17",
        "disconnected",
        "kicked by this experience",
        "you have been kicked",
        "profile session end",
        "please rejoin",
        "moderation message",
    }

    for _, marker in ipairs(markers) do
        if string.find(text, marker, 1, true) then
            return true
        end
    end

    return false
end

local function isSimpleConnectionFailureMessage(message)
    local text = string.lower(tostring(message or ""))
    if text == "" then
        return false
    end

    local markers = {
        "error code: 279",
        "failed to connect to the experience",
        "no response from server",
        "connection failed",
        "server unavailable",
        "teleport failed",
        "failed to teleport",
        "error code: 17",
        "error code: 772",
        "error code: 773",
    }
    for _, marker in ipairs(markers) do
        if string.find(text, marker, 1, true) then
            return true
        end
    end
    return false
end

local function triggerSimpleFallbackTeleport(reason)
    if profileSessionHoldUntil > tick() then
        return false
    end

    local minGap = math.max(0.5, tonumber(SIMPLE_REJOIN_MIN_GAP_SECONDS) or 2)
    if (tick() - simpleFallbackLastTeleportAt) < minGap then
        return false
    end

    simpleFallbackLastTeleportAt = tick()
    lastHopActionAt = tick()
    clearRobloxLoadingUi()
    if USE_BLANK_TELEPORT_GUI then
        setBlankTeleportGui()
    end
    markCurrentServerAsBad("simple_fallback:" .. tostring(reason))
    pcall(function()
        TeleportService:Teleport(TargetPlaceId, LocalPlayer)
    end)
    joinerDebugWarn("simple fallback teleport: " .. tostring(reason))
    return true
end

local function detectConnectionFailurePrompt()
    local okScan, isFailed, promptText, retryButton, leaveButton = runWithElevatedIdentity(function()
        local guiRoot = getCoreGuiReference()
        local roots = {}
        local seenRoots = {}
        local function pushRoot(root)
            if typeof(root) == "Instance" and not seenRoots[root] then
                seenRoots[root] = true
                table.insert(roots, root)
            end
        end

        pushRoot(guiRoot:FindFirstChild("RobloxPromptGui"))
        pushRoot(guiRoot:FindFirstChild("RobloxGui"))

        local playerGui = LocalPlayer and LocalPlayer:FindFirstChild("PlayerGui")
        if playerGui then
            pushRoot(playerGui:FindFirstChild("RobloxPromptGui"))
        end

        for _, child in ipairs(guiRoot:GetChildren()) do
            if child:IsA("ScreenGui") then
                local nameLower = string.lower(tostring(child.Name or ""))
                if nameLower:find("prompt", 1, true) or nameLower:find("error", 1, true) or nameLower:find("disconnect", 1, true) then
                    pushRoot(child)
                end
            end
        end

        if #roots == 0 then
            return false, "", nil, nil
        end

        local textParts = {}
        local retryBtn = nil
        local leaveBtn = nil

        for _, promptRoot in ipairs(roots) do
            for _, desc in ipairs(promptRoot:GetDescendants()) do
                if desc:IsA("TextLabel") or desc:IsA("TextButton") then
                    local raw = tostring(desc.Text or "")
                    local lowered = string.lower(raw)
                    if lowered ~= "" then
                        table.insert(textParts, lowered)
                    end
                    if desc:IsA("TextButton") then
                        if lowered == "retry" then
                            retryBtn = retryBtn or desc
                        elseif lowered == "leave" or lowered == "ok" then
                            leaveBtn = leaveBtn or desc
                        end
                    end
                end
            end
        end

        local combined = table.concat(textParts, " ")
        if combined == "" then
            return false, "", retryBtn, leaveBtn
        end

        if shouldAutoRejoinForErrorMessage(combined) then
            return true, combined, retryBtn, leaveBtn
        end
        return false, combined, retryBtn, leaveBtn
    end)

    if not okScan then
        return false, "", nil, nil
    end
    return isFailed, promptText, retryButton, leaveButton
end

if REMOVE_LOADING_SCREEN then
    pcall(function()
        ReplicatedFirst:RemoveDefaultLoadingScreen()
    end)
    clearRobloxLoadingUi()
    if USE_BLANK_TELEPORT_GUI then
        setBlankTeleportGui()
    end

    task.spawn(function()
        local deadline = os.clock() + 12
        while os.clock() < deadline do
            clearRobloxLoadingUi()
            task.wait(0.2)
        end
    end)
end

local function optimizeInstanceForLowGraphics(instance)
    if typeof(instance) ~= "Instance" then
        return
    end

    if MUTE_ALL_AUDIO and instance:IsA("Sound") then
        pcall(function()
            instance.Volume = 0
            instance.Playing = false
            instance.PlaybackSpeed = 1
        end)
    end

    if REMOVE_PARTICLES then
        if instance:IsA("ParticleEmitter") or instance:IsA("Trail") or instance:IsA("Beam") then
            pcall(function()
                instance.Enabled = false
            end)
        elseif instance:IsA("Smoke") or instance:IsA("Fire") or instance:IsA("Sparkles") then
            pcall(function()
                instance.Enabled = false
            end)
        end
    end

    if REMOVE_TEXTURES then
        if instance:IsA("Decal") or instance:IsA("Texture") then
            pcall(function()
                instance.Transparency = 1
            end)
            pcall(function()
                instance.Texture = ""
            end)
        elseif instance:IsA("SurfaceAppearance") then
            pcall(function()
                instance:Destroy()
            end)
        elseif instance:IsA("SpecialMesh") then
            pcall(function()
                instance.TextureId = ""
            end)
        elseif instance:IsA("MeshPart") then
            pcall(function()
                instance.TextureID = ""
            end)
            pcall(function()
                instance.Material = Enum.Material.SmoothPlastic
            end)
            pcall(function()
                instance.CastShadow = false
            end)
        elseif instance:IsA("BasePart") then
            pcall(function()
                instance.Material = Enum.Material.SmoothPlastic
            end)
            pcall(function()
                instance.CastShadow = false
            end)
        end
    end
end

local function applyPerformanceMode()
    if not PERFORMANCE_MODE then
        return
    end

    local ReplicatedFirst = game:GetService("ReplicatedFirst")
    local Lighting = game:GetService("Lighting")
    local RunService = game:GetService("RunService")
    local SoundService = game:GetService("SoundService")
    local Terrain = workspace:FindFirstChildOfClass("Terrain")

    if REMOVE_LOADING_SCREEN then
        pcall(function()
            ReplicatedFirst:RemoveDefaultLoadingScreen()
        end)
        clearRobloxLoadingUi()
        if USE_BLANK_TELEPORT_GUI then
            setBlankTeleportGui()
        end
    end

    if type(setfpscap) == "function" and tonumber(FPS_CAP) and FPS_CAP > 0 then
        pcall(function()
            setfpscap(FPS_CAP)
        end)
    end

    if DISABLE_3D_RENDERING and type(RunService.Set3dRenderingEnabled) == "function" then
        pcall(function()
            RunService:Set3dRenderingEnabled(false)
        end)
    end

    pcall(function()
        local renderSettings = settings().Rendering
        if renderSettings then
            renderSettings.QualityLevel = Enum.QualityLevel.Level01
        end
    end)

    pcall(function()
        Lighting.GlobalShadows = false
        Lighting.FogEnd = 9e9
        Lighting.Brightness = 0
        Lighting.EnvironmentDiffuseScale = 0
        Lighting.EnvironmentSpecularScale = 0
    end)

    if REMOVE_POST_EFFECTS then
        for _, effect in ipairs(Lighting:GetChildren()) do
            if effect:IsA("PostEffect") or effect:IsA("Atmosphere") then
                pcall(function()
                    effect.Enabled = false
                end)
                pcall(function()
                    effect:Destroy()
                end)
            end
        end

        pcall(function()
            Lighting.DescendantAdded:Connect(function(desc)
                if desc:IsA("PostEffect") or desc:IsA("Atmosphere") then
                    pcall(function()
                        desc.Enabled = false
                    end)
                    pcall(function()
                        desc:Destroy()
                    end)
                end
            end)
        end)
    end

    if Terrain then
        pcall(function()
            Terrain.WaterWaveSize = 0
            Terrain.WaterWaveSpeed = 0
            Terrain.WaterReflectance = 0
            Terrain.WaterTransparency = 1
            Terrain.Decoration = false
        end)
    end

    if MUTE_ALL_AUDIO then
        pcall(function()
            SoundService.Volume = 0
        end)
        for _, desc in ipairs(game:GetDescendants()) do
            optimizeInstanceForLowGraphics(desc)
        end
        pcall(function()
            game.DescendantAdded:Connect(function(desc)
                optimizeInstanceForLowGraphics(desc)
            end)
        end)
    else
        for _, desc in ipairs(workspace:GetDescendants()) do
            optimizeInstanceForLowGraphics(desc)
        end
        pcall(function()
            workspace.DescendantAdded:Connect(function(desc)
                optimizeInstanceForLowGraphics(desc)
            end)
        end)
    end
end

task.spawn(applyPerformanceMode)

local function readIgnoreMapFromFile(filePath)
    local out = {}
    if type(filePath) ~= "string" or filePath == "" or not isfile(filePath) then
        return out
    end

    local okDecode, decoded = pcall(function()
        return HttpService:JSONDecode(readfile(filePath))
    end)
    if not okDecode or type(decoded) ~= "table" or tonumber(decoded.gameId) ~= tonumber(TargetPlaceId) then
        return out
    end

    local rawServers = decoded.servers
    local nowSeconds = getNowSeconds()
    if type(rawServers) == "table" then
        for jobId, expiresAt in pairs(rawServers) do
            if type(jobId) == "string" and jobId ~= "" and type(expiresAt) == "number" and (expiresAt == JOINER_PERMANENT_BLOCK_SENTINEL or expiresAt > nowSeconds) then
                out[jobId] = expiresAt
            end
        end
    end
    return out
end

local function persistIgnoreMapToFile(filePath, map, mergeExisting)
    if type(filePath) ~= "string" or filePath == "" then
        return
    end

    local merged = {}
    if mergeExisting then
        local existing = readIgnoreMapFromFile(filePath)
        for jobId, expiresAt in pairs(existing) do
            merged[jobId] = expiresAt
        end
    end

    for jobId, expiresAt in pairs(map) do
        if type(jobId) == "string" and jobId ~= "" and type(expiresAt) == "number" then
            local prev = merged[jobId]
            if expiresAt == JOINER_PERMANENT_BLOCK_SENTINEL then
                merged[jobId] = JOINER_PERMANENT_BLOCK_SENTINEL
            elseif prev ~= JOINER_PERMANENT_BLOCK_SENTINEL and (type(prev) ~= "number" or expiresAt > prev) then
                merged[jobId] = expiresAt
            end
        end
    end

    local payload = {
        gameId = TargetPlaceId,
        servers = {},
    }

    local nowSeconds = getNowSeconds()
    for jobId, expiresAt in pairs(merged) do
        if type(jobId) == "string" and jobId ~= "" and type(expiresAt) == "number" and (expiresAt == JOINER_PERMANENT_BLOCK_SENTINEL or expiresAt > nowSeconds) then
            payload.servers[jobId] = expiresAt
        end
    end

    local okEncode, encoded = pcall(HttpService.JSONEncode, HttpService, payload)
    if okEncode then
        writefile(filePath, encoded)
    end
end

local function persistIgnoreList()
    persistIgnoreMapToFile(IGNORE_FILE, attemptedServerIds, false)
    if ENABLE_SHARED_CROSS_ACCOUNT_BLOCKLIST then
        -- Merge on write so concurrent accounts preserve each other's recent blocks.
        persistIgnoreMapToFile(SHARED_IGNORE_FILE, sharedAttemptedServerIds, true)
    end
end

local function mergeIgnoreMaps(target, source)
    if type(target) ~= "table" or type(source) ~= "table" then
        return
    end

    local nowSeconds = getNowSeconds()
    for jobId, expiresAt in pairs(source) do
        if type(jobId) == "string" and jobId ~= "" and type(expiresAt) == "number" then
            if expiresAt == JOINER_PERMANENT_BLOCK_SENTINEL then
                target[jobId] = JOINER_PERMANENT_BLOCK_SENTINEL
            elseif expiresAt > nowSeconds then
                local prev = target[jobId]
                if prev ~= JOINER_PERMANENT_BLOCK_SENTINEL and (type(prev) ~= "number" or expiresAt > prev) then
                    target[jobId] = expiresAt
                end
            end
        end
    end
end

local function refreshSharedIgnoreMapIfNeeded(force)
    if not ENABLE_SHARED_CROSS_ACCOUNT_BLOCKLIST then
        return
    end

    local nowTick = tick()
    local refreshEvery = math.max(0.1, tonumber(SHARED_IGNORE_REFRESH_SECONDS) or 0.5)
    if not force and (nowTick - lastSharedIgnoreRefreshAt) < refreshEvery then
        return
    end
    lastSharedIgnoreRefreshAt = nowTick

    local fromDisk = readIgnoreMapFromFile(SHARED_IGNORE_FILE)
    if type(fromDisk) == "table" then
        mergeIgnoreMaps(sharedAttemptedServerIds, fromDisk)
    end
end

local function sanitizeJobIdForFile(jobId)
    return tostring(jobId or ""):gsub("[^%w%-_]", "_")
end

local function getSharedServerClaimFile(jobId)
    return SHARED_SERVER_CLAIM_FILE_PREFIX .. sanitizeJobIdForFile(jobId) .. ".json"
end

local function writeSharedServerClaim(jobId)
    local payload = {
        gameId = TargetPlaceId,
        jobId = tostring(jobId or ""),
        owner = SHARED_SERVER_CLAIM_OWNER,
        expiresAt = getNowSeconds() + math.max(1, tonumber(SHARED_SERVER_BLOCK_SECONDS) or JOINER_SERVER_BLOCK_SECONDS),
    }

    local okEncode, encoded = pcall(HttpService.JSONEncode, HttpService, payload)
    if not okEncode or type(encoded) ~= "string" then
        return false
    end

    local claimFile = getSharedServerClaimFile(jobId)
    local okWrite = pcall(function()
        writefile(claimFile, encoded)
    end)
    return okWrite
end

local function isSharedServerClaimOwnedBySelf(jobId)
    local claimFile = getSharedServerClaimFile(jobId)
    if not isfile(claimFile) then
        return false
    end

    local okDecode, decoded = pcall(function()
        return HttpService:JSONDecode(readfile(claimFile))
    end)
    if not okDecode or type(decoded) ~= "table" then
        return false
    end

    if tonumber(decoded.gameId) ~= tonumber(TargetPlaceId) then
        return false
    end
    if tostring(decoded.jobId or "") ~= tostring(jobId or "") then
        return false
    end
    if type(decoded.expiresAt) ~= "number" or decoded.expiresAt <= getNowSeconds() then
        return false
    end

    return tostring(decoded.owner or "") == SHARED_SERVER_CLAIM_OWNER
end

local function loadIgnoreList()
    attemptedServerIds = readIgnoreMapFromFile(IGNORE_FILE)
    if ENABLE_SHARED_CROSS_ACCOUNT_BLOCKLIST then
        sharedAttemptedServerIds = readIgnoreMapFromFile(SHARED_IGNORE_FILE)
        lastSharedIgnoreRefreshAt = tick()
    else
        sharedAttemptedServerIds = {}
    end
    persistIgnoreList()
end

local function urlEncodeBasic(text)
    return tostring(text or ""):gsub("([^%w%-_%.~])", function(char)
        return string.format("%%%02X", string.byte(char))
    end)
end

local function getRamServerClaimEndpoint()
    local base = "http://localhost:" .. tostring(RAM_STATUS_PUSH_PORT) .. "/ClaimServer"
    if tostring(RAM_STATUS_PUSH_PASSWORD or "") == "" then
        return base
    end

    return base .. "?Password=" .. urlEncodeBasic(RAM_STATUS_PUSH_PASSWORD)
end

local function tryClaimServerViaRam(jobId)
    if not RAM_SERVER_CLAIM_ENABLED then
        return nil, "disabled"
    end

    local requestFn = request or http_request or httprequest or (syn and syn.request) or (http and http.request) or (fluxus and fluxus.request)
    if type(requestFn) ~= "function" then
        return nil, "no_request_function"
    end

    local leaseSeconds = math.max(10, tonumber(RAM_SERVER_CLAIM_LEASE_SECONDS) or tonumber(SHARED_SERVER_BLOCK_SECONDS) or JOINER_SERVER_BLOCK_SECONDS)
    local payload = {
        Account = tostring(LocalPlayer.Name or ""),
        Username = tostring(LocalPlayer.Name or ""),
        UserId = tostring(LocalPlayer.UserId or ""),
        PlaceId = tostring(TargetPlaceId or game.PlaceId or 0),
        JobId = tostring(jobId or ""),
        LeaseSeconds = leaseSeconds,
    }

    local okEncode, body = pcall(HttpService.JSONEncode, HttpService, payload)
    if not okEncode then
        return nil, "encode_failed"
    end

    local endpoint = getRamServerClaimEndpoint()
    local headers = { ["Content-Type"] = "application/json" }
    local variants = {
        { Url = endpoint, Method = "POST", Headers = headers, Body = body },
        { url = endpoint, method = "POST", headers = headers, body = body },
    }

    local response = nil
    local lastError = "request_failed"
    for _, options in ipairs(variants) do
        local okReq, res = pcall(requestFn, options)
        if okReq and res ~= nil then
            response = res
            break
        end
        if not okReq then
            lastError = tostring(res)
        end
    end

    if not response then
        return nil, lastError
    end

    local statusCode = tonumber(response.StatusCode or response.status or response.Status or response.code)
    if statusCode and (statusCode < 200 or statusCode >= 300) then
        return nil, "http_" .. tostring(statusCode)
    end

    local bodyRaw = response.Body or response.body
    if type(bodyRaw) == "table" then
        if bodyRaw.claimed == true then
            return true, nil
        end
        if bodyRaw.claimed == false then
            return false, tostring(bodyRaw.owner or "")
        end
    elseif type(bodyRaw) == "string" and bodyRaw ~= "" then
        local okDecode, decoded = pcall(HttpService.JSONDecode, HttpService, bodyRaw)
        if okDecode and type(decoded) == "table" then
            if decoded.claimed == true then
                return true, nil
            end
            if decoded.claimed == false then
                return false, tostring(decoded.owner or "")
            end
        end
        if bodyRaw == "true" then
            return true, nil
        end
    end

    return nil, "invalid_response"
end

local function setBlockOnMap(map, jobId, seconds, allowPermanent)
    if type(map) ~= "table" then
        return
    end
    local n = tonumber(seconds)
    if allowPermanent and n == JOINER_PERMANENT_BLOCK_SENTINEL then
        map[jobId] = JOINER_PERMANENT_BLOCK_SENTINEL
    else
        map[jobId] = getNowSeconds() + math.max(1, n or JOINER_SERVER_BLOCK_SECONDS)
    end
end

local function markServerBlocked(jobId, seconds)
    jobId = tostring(jobId or "")
    if jobId == "" then
        return
    end

    setBlockOnMap(attemptedServerIds, jobId, seconds, true)

    if ENABLE_SHARED_CROSS_ACCOUNT_BLOCKLIST then
        local sharedSeconds = seconds
        if tonumber(sharedSeconds) == JOINER_PERMANENT_BLOCK_SENTINEL then
            sharedSeconds = math.max(1, tonumber(SHARED_SERVER_BLOCK_SECONDS) or JOINER_SERVER_BLOCK_SECONDS)
        end
        setBlockOnMap(sharedAttemptedServerIds, jobId, sharedSeconds, false)
    end

    persistIgnoreList()
end

local function markServerBlockedForever(jobId)
    markServerBlocked(jobId, JOINER_PERMANENT_BLOCK_SENTINEL)
end

local function markCurrentServerAsBad(reason)
    local currentJobId = tostring(game.JobId or "")
    if currentJobId == "" then
        return
    end
    markServerBlockedForever(currentJobId)
    joinerDebugWarn("blocked current job=" .. currentJobId .. " reason=" .. tostring(reason))
end

local function isBlockedInMap(map, jobId)
    if type(map) ~= "table" then
        return false, false
    end

    local expiresAt = map[jobId]
    if type(expiresAt) ~= "number" then
        return false, false
    end
    if expiresAt == JOINER_PERMANENT_BLOCK_SENTINEL then
        return true, false
    end

    if expiresAt <= getNowSeconds() then
        map[jobId] = nil
        return false, true
    end

    return true, false
end

local function isServerBlocked(jobId)
    jobId = tostring(jobId or "")
    if jobId == "" then
        return false
    end

    local blockedLocal, localDirty = isBlockedInMap(attemptedServerIds, jobId)
    local blockedShared, sharedDirty = false, false
    if ENABLE_SHARED_CROSS_ACCOUNT_BLOCKLIST then
        refreshSharedIgnoreMapIfNeeded(false)
        blockedShared, sharedDirty = isBlockedInMap(sharedAttemptedServerIds, jobId)
    end

    if localDirty or sharedDirty then
        persistIgnoreList()
    end

    return blockedLocal or blockedShared
end

local function tryClaimServerForHop(jobId)
    jobId = tostring(jobId or "")
    if jobId == "" then
        return false
    end

    if isServerBlocked(jobId) then
        return false
    end

    local ramClaimed, ramReason = tryClaimServerViaRam(jobId)
    if ramClaimed == true then
        markServerBlocked(jobId, math.max(1, tonumber(SHARED_SERVER_BLOCK_SECONDS) or JOINER_SERVER_BLOCK_SECONDS))
        return true
    end
    if ramClaimed == false then
        markServerBlocked(jobId, math.max(1, tonumber(SHARED_SERVER_BLOCK_SECONDS) or JOINER_SERVER_BLOCK_SECONDS))
        joinerDebugWarn("ram claim denied target=" .. jobId .. " owner=" .. tostring(ramReason or "unknown"))
        return false
    end

    if not ENABLE_SHARED_CROSS_ACCOUNT_BLOCKLIST then
        markServerBlocked(jobId, JOINER_SERVER_BLOCK_SECONDS)
        return true
    end

    refreshSharedIgnoreMapIfNeeded(true)
    if isServerBlocked(jobId) then
        return false
    end

    if not writeSharedServerClaim(jobId) then
        return false
    end

    task.wait(math.max(0, tonumber(SHARED_SERVER_CLAIM_WAIT_SECONDS) or 0.08))
    if not isSharedServerClaimOwnedBySelf(jobId) then
        return false
    end

    markServerBlocked(jobId, math.max(1, tonumber(SHARED_SERVER_BLOCK_SECONDS) or JOINER_SERVER_BLOCK_SECONDS))

    -- Final ownership check to avoid teleport races where another instance overrides the claim
    -- between our initial claim-read and the actual teleport call.
    task.wait(0.03)
    if not isSharedServerClaimOwnedBySelf(jobId) then
        return false
    end

    return true
end

pcall(function()
    TeleportService.TeleportInitFailed:Connect(function(player, teleportResult, errorMessage)
        if player ~= LocalPlayer then
            return
        end

        local teleportFailureReason = tostring(errorMessage or teleportResult or "teleport_init_failed")
        local failedJobId = lastTeleportTargetJobId
        if failedJobId then
            joinerDebugWarn("teleport init failed target=" .. tostring(failedJobId) .. " reason=" .. tostring(teleportFailureReason))
            -- Avoid re-attempting the same bad/full instance repeatedly.
            markServerBlockedForever(failedJobId)
        end

        if AUTO_REJOIN_ON_TELEPORT_FAIL then
            queueAutoRejoin("teleport_init_failed:" .. teleportFailureReason)
        end
    end)
end)

if AUTO_REJOIN_ON_ERROR_MESSAGE then
    pcall(function()
        GuiService.ErrorMessageChanged:Connect(function(message)
            if isSimpleConnectionFailureMessage(message) then
                simpleFallbackLastErrorAt = tick()
                simpleFallbackLastErrorText = tostring(message or "")
                triggerSimpleFallbackTeleport("gui_error_simple")
            end
            if shouldAutoRejoinForErrorMessage(message) then
                local isProfileSession = isProfileSessionErrorMessage(message)
                if isProfileSession then
                    markProfileSessionHold("gui_error")
                end
                local immediate = isImmediateRejoinErrorMessage(message) and not isProfileSession
                if immediate then
                    markCurrentServerAsBad("gui_error_immediate")
                    startImmediateRejoinBurst("gui_error:" .. tostring(message))
                end
                local delayOverride = nil
                if isProfileSession and profileSessionHoldUntil > tick() then
                    delayOverride = profileSessionHoldUntil - tick()
                end
                queueAutoRejoin("gui_error:" .. tostring(message), true, immediate, delayOverride)
            end
        end)
    end)
end

if AUTO_REJOIN_PROMPT_WATCHDOG then
    task.spawn(function()
        while task.wait(math.max(0.05, tonumber(AUTO_REJOIN_PROMPT_SCAN_INTERVAL) or 0.75)) do
            if autoRejoinInProgress and autoRejoinStartedAt > 0 and (tick() - autoRejoinStartedAt) > 6 then
                autoRejoinInProgress = false
                autoRejoinStartedAt = 0
            end
            local isFailed, promptText, retryButton, leaveButton = detectConnectionFailurePrompt()
            if isFailed then
                if isSimpleConnectionFailureMessage(promptText) then
                    simpleFallbackLastErrorAt = tick()
                    simpleFallbackLastErrorText = tostring(promptText or "")
                    triggerSimpleFallbackTeleport("prompt_watchdog_simple")
                end
                local isProfileSession = isProfileSessionErrorMessage(promptText)
                if isProfileSession then
                    markProfileSessionHold("prompt_watchdog")
                end
                local immediate = isImmediateRejoinErrorMessage(promptText) and not isProfileSession
                if immediate then
                    markCurrentServerAsBad("prompt_watchdog_immediate")
                end
                if retryButton and retryButton.Parent then
                    triggerButtonSignal(retryButton)
                end
                if immediate and leaveButton and leaveButton.Parent then
                    triggerButtonSignal(leaveButton)
                    -- In leave-only 267 dialogs, spam activation shortly to force transition.
                    task.spawn(function()
                        local deadline = tick() + 1.25
                        while tick() < deadline do
                            if not (leaveButton and leaveButton.Parent) then
                                break
                            end
                            triggerButtonSignal(leaveButton)
                            task.wait(0.1)
                        end
                    end)
                end
                if immediate then
                    startImmediateRejoinBurst("prompt_watchdog:" .. tostring(promptText):sub(1, 80))
                end
                local delayOverride = nil
                if isProfileSession and profileSessionHoldUntil > tick() then
                    delayOverride = profileSessionHoldUntil - tick()
                end
                queueAutoRejoin("prompt_watchdog:" .. tostring(promptText):sub(1, 80), true, immediate, delayOverride)
            end
        end
    end)
end

if ENABLE_KICK_HOOK_REJOIN then
pcall(function()
    local kickHookGuard = false

    local function shouldInterceptKickCall(self)
        if self ~= LocalPlayer then
            return false
        end
        if type(checkcaller) == "function" and checkcaller() then
            return false
        end
        return true
    end

    local function handleInterceptedKick(sourceTag, reasonText)
        if kickHookGuard then
            return
        end
        kickHookGuard = true
        local profileSession = isProfileSessionErrorMessage(reasonText)
        if profileSession then
            markProfileSessionHold(sourceTag or "kick_hook")
        else
            markCurrentServerAsBad(sourceTag or "kick_hook")
            startImmediateRejoinBurst(sourceTag or "kick_hook")
        end
        local delayOverride = nil
        if profileSession and profileSessionHoldUntil > tick() then
            delayOverride = profileSessionHoldUntil - tick()
        end
        queueAutoRejoin(sourceTag or "kick_hook", true, not profileSession, delayOverride)
        task.delay(math.max(0.2, tonumber(AUTO_REJOIN_KICK_IMMEDIATE_DELAY) or 0.05), function()
            kickHookGuard = false
        end)
    end

    if type(hookfunction) == "function" and LocalPlayer and type(LocalPlayer.Kick) == "function" then
        local oldKick
        oldKick = hookfunction(LocalPlayer.Kick, function(self, ...)
            if shouldInterceptKickCall(self) then
                local args = table.pack(...)
                local reasonText = tostring(args[1] or "")
                handleInterceptedKick("kick_hook_function", reasonText)
                return nil
            end
            return oldKick(self, ...)
        end)
    end

    if type(hookmetamethod) == "function" and type(getnamecallmethod) == "function" and LocalPlayer then
        local oldNamecall
        oldNamecall = hookmetamethod(game, "__namecall", function(self, ...)
            local method = getnamecallmethod()
            if method == "Kick" and shouldInterceptKickCall(self) then
                local args = table.pack(...)
                local reasonText = tostring(args[1] or "")
                handleInterceptedKick("kick_hook_namecall", reasonText)
                return nil
            end
            return oldNamecall(self, ...)
        end)
    end
end)
end

local function safeJsonEncode(value)
    local ok, encoded = pcall(HttpService.JSONEncode, HttpService, value)
    if ok then
        return encoded
    end
    return tostring(value)
end

local function shuffleListInPlace(list)
    if type(list) ~= "table" then
        return
    end

    joinerShuffleNonce = joinerShuffleNonce + 1
    local baseSeed = (tonumber(LocalPlayer.UserId) or 0) + math.floor(os.clock() * 1000) + (joinerShuffleNonce * 97)
    local rng = Random.new(baseSeed)

    for i = #list, 2, -1 do
        local j = rng:NextInteger(1, i)
        list[i], list[j] = list[j], list[i]
    end
end

local function getResponseStatusCode(response)
    if type(response) ~= "table" then
        return nil
    end
    return tonumber(response.StatusCode or response.status or response.Status or response.code)
end

-- ------------------------------
-- Shared helpers
-- ------------------------------
local function firstFunction(candidates)
    for _, candidate in ipairs(candidates) do
        if type(candidate) == "function" then
            return candidate
        end
    end
    return nil
end

local function getRequestFunction()
    return firstFunction({
        request,
        http_request,
        httprequest,
        syn and syn.request,
        http and http.request,
        fluxus and fluxus.request,
    })
end

local function sendJsonRequest(method, url, payload, headers)
    local requestFn = getRequestFunction()
    if not requestFn then
        return nil, "no_request_function"
    end

    local body
    if payload ~= nil then
        local okEncode, encoded = pcall(HttpService.JSONEncode, HttpService, payload)
        if not okEncode then
            return nil, "json_encode_failed:" .. tostring(encoded)
        end
        body = encoded
    end

    local resolvedHeaders = headers or { ["Content-Type"] = "application/json" }
    local variants = {
        {
            Url = url,
            Method = method,
            Headers = resolvedHeaders,
            Body = body,
        },
        {
            url = url,
            method = method,
            headers = resolvedHeaders,
            body = body,
        },
    }

    local lastError = "request_failed"
    for _, options in ipairs(variants) do
        local ok, response = pcall(requestFn, options)
        if ok and response ~= nil then
            return response, nil
        end
        if not ok then
            lastError = tostring(response)
        end
    end

    return nil, lastError
end

local function decodeResponseBody(response)
    if type(response) ~= "table" then
        return nil
    end

    if type(response.json) == "table" then
        return response.json
    end
    if type(response.JSON) == "table" then
        return response.JSON
    end

    local body = response.Body or response.body
    if type(body) == "table" then
        return body
    end
    if type(body) ~= "string" or body == "" then
        return nil
    end

    local okDecode, decoded = pcall(HttpService.JSONDecode, HttpService, body)
    if okDecode then
        return decoded
    end
    return nil
end

-- ------------------------------
-- Joiner request helpers
-- ------------------------------
local function sendJoinerRequest(method, path, payload)
    local base = tostring(JOINER_URL or ""):gsub("/$", "")
    if base == "" then
        return nil, "missing_joiner_url"
    end
    return sendJsonRequest(method, base .. tostring(path), payload, {
        ["Content-Type"] = "application/json",
    })
end

local function urlEncode(text)
    if HttpService and type(HttpService.UrlEncode) == "function" then
        local ok, encoded = pcall(HttpService.UrlEncode, HttpService, tostring(text or ""))
        if ok and type(encoded) == "string" then
            return encoded
        end
    end

    return tostring(text or ""):gsub("([^%w%-_%.~])", function(char)
        return string.format("%%%02X", string.byte(char))
    end)
end

local function getRamStatusEndpoint()
    local base = "http://localhost:" .. tostring(RAM_STATUS_PUSH_PORT) .. "/PushLiveStatus"
    if tostring(RAM_STATUS_PUSH_PASSWORD or "") == "" then
        return base
    end

    return base .. "?Password=" .. urlEncode(RAM_STATUS_PUSH_PASSWORD)
end

local function getCurrentPlaceName()
    local nowTick = tick()
    local placeId = tonumber(game.PlaceId) or tonumber(TargetPlaceId)
    local previousPlaceId = cachedPlaceNamePlaceId

    if type(cachedPlaceName) == "string"
        and cachedPlaceName ~= ""
        and cachedPlaceNamePlaceId == placeId
        and (nowTick - cachedPlaceNameLookupAt) < 120 then
        return cachedPlaceName
    end

    cachedPlaceNameLookupAt = nowTick
    cachedPlaceNamePlaceId = placeId

    local okInfo, info = pcall(function()
        return MarketplaceService:GetProductInfo(placeId, Enum.InfoType.Asset)
    end)

    if okInfo and type(info) == "table" and type(info.Name) == "string" and info.Name ~= "" then
        cachedPlaceName = info.Name
    elseif previousPlaceId ~= placeId then
        cachedPlaceName = ""
    end

    return cachedPlaceName
end

local function pushLiveStatusToRam(force, inGameOverride)
    if not RAM_STATUS_PUSH_ENABLED then
        return false, "disabled"
    end

    local nowTick = tick()
    local interval = math.max(0.25, tonumber(RAM_STATUS_PUSH_INTERVAL) or 1)
    if not force and (nowTick - lastRamStatusPushAt) < interval then
        return false, "cooldown"
    end

    local detectedPlaceId = tonumber(game.PlaceId)
    local detectedJobId = tostring(game.JobId or "")
    local okLoaded, detectedLoaded = pcall(function()
        return game:IsLoaded()
    end)
    detectedLoaded = okLoaded and detectedLoaded == true
    local detectedInGame = detectedLoaded and (detectedPlaceId or 0) > 0 and detectedJobId ~= ""

    local inGame = inGameOverride
    if type(inGame) ~= "boolean" then
        inGame = detectedInGame
    end

    local placeId = detectedPlaceId or tonumber(TargetPlaceId)
    if inGame and (not placeId or placeId <= 0) then
        inGame = false
    end

    local gameName = ""
    if inGame then
        gameName = tostring(getCurrentPlaceName() or "")
        if gameName == "" and placeId and placeId > 0 then
            gameName = tostring(placeId)
        end
    end

    local payload = {
        Account = tostring(LocalPlayer.Name or ""),
        Username = tostring(LocalPlayer.Name or ""),
        UserId = tostring(LocalPlayer.UserId or ""),
        HasOpenInstance = true,
        IsOnServer = inGame,
        InGame = inGame,
        PlaceId = inGame and tostring(placeId or "") or "",
        GameName = gameName,
        JobId = inGame and tostring(game.JobId or "") or "",
    }

    local response, requestErr = sendJsonRequest("POST", getRamStatusEndpoint(), payload, {
        ["Content-Type"] = "application/json",
    })

    if not response then
        if (nowTick - lastRamStatusPushErrorAt) > 10 then
            lastRamStatusPushErrorAt = nowTick
            warn("[Joiner RAM Status] push failed: " .. tostring(requestErr))
        end
        return false, requestErr or "request_failed"
    end

    local statusCode = getResponseStatusCode(response)
    if statusCode and statusCode >= 200 and statusCode < 300 then
        lastRamStatusPushAt = nowTick
        return true, nil
    end

    local body = tostring(response.Body or response.body or "")
    if body == "true" then
        lastRamStatusPushAt = nowTick
        return true, nil
    end

    if (nowTick - lastRamStatusPushErrorAt) > 10 then
        lastRamStatusPushErrorAt = nowTick
        warn("[Joiner RAM Status] push failed with status " .. tostring(statusCode))
    end

    return false, "non_ok_response"
end

-- ------------------------------
-- Brainrot collection
-- ------------------------------
local function safeRequire(instance)
    if typeof(instance) ~= "Instance" then
        return nil
    end
    local ok, result = pcall(require, instance)
    if ok then
        return result
    end
    return nil
end

local function shouldRetryModuleLoad(lastAttemptAt)
    if type(lastAttemptAt) ~= "number" then
        return true
    end
    return (tick() - lastAttemptAt) >= JOINER_MODULE_RETRY_SECONDS
end

local function getAnimalsData()
    if type(animalsDataCache) == "table" then
        return animalsDataCache
    end
    if animalsDataCache == false and not shouldRetryModuleLoad(animalsDataLastAttemptAt) then
        return nil
    end
    animalsDataLastAttemptAt = tick()
    local datas = ReplicatedStorage:FindFirstChild("Datas") or ReplicatedStorage:WaitForChild("Datas", 10)
    local animalsModule = datas and (datas:FindFirstChild("Animals") or datas:WaitForChild("Animals", 5)) or nil
    local resolved = safeRequire(animalsModule)
    if type(resolved) == "table" then
        animalsDataCache = resolved
    else
        animalsDataCache = false
    end
    return animalsDataCache or nil
end

local function getAnimalsShared()
    if type(animalsSharedCache) == "table" then
        return animalsSharedCache
    end
    if animalsSharedCache == false and not shouldRetryModuleLoad(animalsSharedLastAttemptAt) then
        return nil
    end
    animalsSharedLastAttemptAt = tick()
    local shared = ReplicatedStorage:FindFirstChild("Shared") or ReplicatedStorage:WaitForChild("Shared", 10)
    local animalsModule = shared and (shared:FindFirstChild("Animals") or shared:WaitForChild("Animals", 5)) or nil
    local resolved = safeRequire(animalsModule)
    if type(resolved) == "table" then
        animalsSharedCache = resolved
    else
        animalsSharedCache = false
    end
    return animalsSharedCache or nil
end

local function getPlotController()
    if type(plotControllerCache) == "table" then
        return plotControllerCache or nil
    end
    if plotControllerCache == false and not shouldRetryModuleLoad(plotControllerLastAttemptAt) then
        return nil
    end
    plotControllerLastAttemptAt = tick()

    local controllers = ReplicatedStorage:FindFirstChild("Controllers") or ReplicatedStorage:WaitForChild("Controllers", 10)
    local plotControllerModule = controllers and (controllers:FindFirstChild("PlotController") or controllers:WaitForChild("PlotController", 5)) or nil
    local resolved = safeRequire(plotControllerModule)
    if type(resolved) == "table" then
        plotControllerCache = resolved
    else
        plotControllerCache = false
    end
    return plotControllerCache or nil
end

local function getLocalPlotOwnershipState()
    local plotController = getPlotController()
    if type(plotController) ~= "table" then
        return nil, 0, 0
    end

    if type(plotController.GetMyPlot) == "function" then
        local okMyPlot, myPlot = pcall(plotController.GetMyPlot, plotController)
        if okMyPlot and myPlot ~= nil then
            return true, 1, 0
        end
    end

    if type(plotController.GetPlots) == "function" then
        local okPlots, plots = pcall(plotController.GetPlots, plotController)
        if okPlots and type(plots) == "table" then
            local totalPlots = 0
            local occupiedPlots = 0
            for _, plot in pairs(plots) do
                if type(plot) == "table" and type(plot.GetOwner) == "function" then
                    totalPlots = totalPlots + 1
                    local okOwner, owner = pcall(plot.GetOwner, plot)
                    if okOwner then
                        if owner == LocalPlayer or owner == LocalPlayer.UserId then
                            return true, totalPlots, occupiedPlots
                        end
                        if owner ~= nil then
                            occupiedPlots = occupiedPlots + 1
                        end
                    end
                end
            end
            if totalPlots > 0 then
                return false, totalPlots, occupiedPlots
            end
        end
    end

    return nil, 0, 0
end

local function getDisplayName(index)
    if type(index) ~= "string" or index == "" then
        return "Unknown"
    end
    local animalsData = getAnimalsData()
    local entry = type(animalsData) == "table" and animalsData[index] or nil
    if type(entry) == "table" and type(entry.DisplayName) == "string" and entry.DisplayName ~= "" then
        return entry.DisplayName
    end
    return index
end

local function getMoneyPerSec(entry)
    if type(entry) ~= "table" then
        return 0
    end

    local idx = entry.Index
    if type(idx) ~= "string" then
        return 0
    end

    if type(entry.Generation) == "number" then
        return entry.Generation
    end
    if type(entry.MoneyPerSec) == "number" then
        return entry.MoneyPerSec
    end

    local animalsShared = getAnimalsShared()
    if type(animalsShared) == "table" and type(animalsShared.GetGeneration) == "function" then
        local okA, valueA = pcall(animalsShared.GetGeneration, animalsShared, idx, entry.Mutation, entry.Traits, nil)
        if okA and type(valueA) == "number" then
            return valueA
        end
        local okB, valueB = pcall(animalsShared.GetGeneration, idx, entry.Mutation, entry.Traits, nil)
        if okB and type(valueB) == "number" then
            return valueB
        end
    end

    local animalsData = getAnimalsData()
    local info = type(animalsData) == "table" and animalsData[idx] or nil
    if type(info) == "table" and type(info.Generation) == "number" then
        return info.Generation
    end

    return 0
end

local function getGlobal(name)
    if type(getgenv) == "function" then
        local env = getgenv()
        if type(env) == "table" then
            local value = env[name]
            if value ~= nil then
                return value
            end
        end
    end
    if type(getfenv) == "function" then
        local ok, env = pcall(getfenv, 0)
        if ok and type(env) == "table" then
            return env[name]
        end
    end
    return nil
end

local function parseAbbrevNumber(text)
    if type(text) ~= "string" or text == "" then
        return nil
    end

    local compact = text:upper():gsub(",", "")
    local value, suffix = compact:match("([%d%.]+)%s*([KMBT]?)")
    if not value then
        return nil
    end

    local n = tonumber(value)
    if not n then
        return nil
    end

    local mul = 1
    if suffix == "K" then
        mul = 1e3
    elseif suffix == "M" then
        mul = 1e6
    elseif suffix == "B" then
        mul = 1e9
    elseif suffix == "T" then
        mul = 1e12
    end
    return n * mul
end

local function collectFromPlotController()
    local results = {}
    local seen = {}
    local plotController = getPlotController()

    if type(plotController) ~= "table" or type(plotController.GetPlots) ~= "function" then
        return results
    end

    local okPlots, plots = pcall(plotController.GetPlots, plotController)
    if not okPlots or type(plots) ~= "table" then
        return results
    end

    for plotUID, plot in pairs(plots) do
        if type(plot) == "table" then
            local channel = rawget(plot, "Channel")
            local plotModel = rawget(plot, "PlotModel")
            local podiums = typeof(plotModel) == "Instance" and plotModel:FindFirstChild("AnimalPodiums") or nil
            if channel and type(channel.Get) == "function" and podiums then
                local okList, animalList = pcall(channel.Get, channel, "AnimalList")
                if okList and type(animalList) == "table" then
                    for slot, entry in pairs(animalList) do
                        if type(entry) == "table" and type(entry.Index) == "string" and entry.Index ~= "" then
                            local podium = podiums:FindFirstChild(tostring(slot))
                            local base = podium and podium:FindFirstChild("Base")
                            local spawnPart = base and base:FindFirstChild("Spawn")
                            if spawnPart and spawnPart:IsA("BasePart") then
                                local moneyPerSec = math.max(0, tonumber(getMoneyPerSec(entry)) or 0)
                                local key = table.concat({
                                    tostring(plotUID),
                                    tostring(slot),
                                    tostring(entry.Index),
                                    tostring(entry.Mutation or ""),
                                    tostring(math.floor(moneyPerSec * 100 + 0.5)),
                                }, ":")
                                if not seen[key] then
                                    seen[key] = true
                                    table.insert(results, {
                                        key = key,
                                        name = getDisplayName(entry.Index),
                                        moneyPerSec = moneyPerSec,
                                    })
                                end
                            end
                        end
                    end
                end
            end
        end
    end

    table.sort(results, function(a, b)
        if a.moneyPerSec ~= b.moneyPerSec then
            return a.moneyPerSec > b.moneyPerSec
        end
        return tostring(a.name) < tostring(b.name)
    end)
    return results
end

local function collectFromWorkspaceFallback()
    local results = {}
    local seen = {}

    for _, obj in ipairs(workspace:GetDescendants()) do
        if obj:IsA("Model") then
            local idx = obj:GetAttribute("Index")
            if type(idx) == "string" and idx ~= "" then
                local mps = obj:GetAttribute("Generation")
                if type(mps) ~= "number" then
                    mps = obj:GetAttribute("MoneyPerSec")
                end
                if type(mps) ~= "number" then
                    local generationLabel = obj:FindFirstChild("Generation", true)
                    if generationLabel and generationLabel:IsA("TextLabel") then
                        mps = parseAbbrevNumber(tostring(generationLabel.Text))
                    end
                end
                mps = math.max(0, tonumber(mps) or 0)

                local pp = obj.PrimaryPart or obj:FindFirstChildWhichIsA("BasePart")
                local posTag = "0,0,0"
                if pp then
                    local p = pp.Position
                    posTag = string.format("%d,%d,%d", math.floor(p.X + 0.5), math.floor(p.Y + 0.5), math.floor(p.Z + 0.5))
                end

                local key = tostring(idx) .. ":" .. posTag .. ":" .. tostring(math.floor(mps * 100 + 0.5))
                if not seen[key] then
                    seen[key] = true
                    table.insert(results, {
                        key = key,
                        name = getDisplayName(idx),
                        moneyPerSec = mps,
                    })
                end
            end
        end
    end

    table.sort(results, function(a, b)
        if a.moneyPerSec ~= b.moneyPerSec then
            return a.moneyPerSec > b.moneyPerSec
        end
        return tostring(a.name) < tostring(b.name)
    end)
    return results
end

local function normalizeBrainrotList(list)
    local out = {}
    local seen = {}
    if type(list) ~= "table" then
        return out
    end

    for _, item in ipairs(list) do
        if type(item) == "table" then
            local key = tostring(item.key or ""):gsub("^%s+", ""):gsub("%s+$", "")
            local name = tostring(item.name or ""):gsub("^%s+", ""):gsub("%s+$", "")
            local mps = math.max(0, tonumber(item.moneyPerSec or item.money_per_sec) or 0)
            if key ~= "" and name ~= "" and not seen[key] then
                seen[key] = true
                table.insert(out, {
                    key = key,
                    name = name,
                    moneyPerSec = mps,
                })
            end
        end
    end

    for _, item in ipairs(out) do
        local key = tostring(item.key or "")
        local mps = math.max(0, tonumber(item.moneyPerSec or 0) or 0)
        if mps > 0 then
            lastGoodMpsByKey[key] = mps
        else
            local lastKnown = lastGoodMpsByKey[key]
            if type(lastKnown) == "number" and lastKnown > 0 then
                item.moneyPerSec = lastKnown
            end
        end
    end

    table.sort(out, function(a, b)
        if a.moneyPerSec ~= b.moneyPerSec then
            return a.moneyPerSec > b.moneyPerSec
        end
        return tostring(a.name) < tostring(b.name)
    end)
    return out
end

local function countPositiveMps(list)
    local count = 0
    if type(list) ~= "table" then
        return 0
    end
    for _, item in ipairs(list) do
        if math.max(0, tonumber(item.moneyPerSec or 0) or 0) > 0 then
            count = count + 1
        end
    end
    return count
end

local function collectBrainrotsForReportRaw()
    local fromPlotController = collectFromPlotController()
    if #fromPlotController > 0 then
        return normalizeBrainrotList(fromPlotController), "plot_controller"
    end

    local provider = getGlobal("JoinerBrainrotProvider")
    if type(provider) == "function" then
        local ok, list = pcall(provider)
        if ok and type(list) == "table" then
            return normalizeBrainrotList(list), "provider"
        end
    end

    return normalizeBrainrotList(collectFromWorkspaceFallback()), "workspace_fallback"
end

local function collectBrainrotsForReport()
    local bestList, bestSource = collectBrainrotsForReportRaw()
    local bestPositive = countPositiveMps(bestList)

    if bestPositive > 0 then
        return bestList, bestSource, bestPositive
    end

    if (tick() - scriptStartedAt) >= JOINER_MPS_INIT_GRACE_SECONDS then
        return bestList, bestSource, bestPositive
    end

    for _ = 1, JOINER_MPS_STABILIZE_RETRIES do
        task.wait(JOINER_MPS_STABILIZE_DELAY)
        local candidateList, candidateSource = collectBrainrotsForReportRaw()
        local candidatePositive = countPositiveMps(candidateList)
        if candidatePositive > bestPositive then
            bestList = candidateList
            bestSource = candidateSource
            bestPositive = candidatePositive
        end
        if bestPositive > 0 then
            break
        end
    end

    return bestList, bestSource, bestPositive
end

local function getExecutorName()
    local identify = firstFunction({ identifyexecutor, getexecutorname })
    if type(identify) == "function" then
        local ok, name = pcall(identify)
        if ok and name then
            return tostring(name)
        end
    end
    return "Unknown"
end

local function reportCurrentServerToJoiner(force)
    local now = tick()
    if not force and (now - lastJoinerReportAt) < JOINER_REPORT_INTERVAL then
        return false, "cooldown"
    end

    local brainrots, source, positiveMpsCount = collectBrainrotsForReport()
    if positiveMpsCount > 0 then
        hadNonZeroMpsReport = true
    end
    if #brainrots > 0 and positiveMpsCount == 0 and not hadNonZeroMpsReport and (now - scriptStartedAt) < JOINER_MPS_INIT_GRACE_SECONDS then
        joinerDebugWarn("report deferred: waiting for mps init source=" .. tostring(source) .. " brainrots=" .. tostring(#brainrots))
        return false, "mps_init_pending"
    end
    local payload = {
        user = LocalPlayer.Name,
        userid = tostring(LocalPlayer.UserId),
        executor = getExecutorName(),
        jobid = tostring(game.JobId or ""),
        placeid = tostring(game.PlaceId or TargetPlaceId),
        playerCount = #Players:GetPlayers(),
        brainrots = brainrots,
    }

    joinerDebugWarn(string.format(
        "report -> user=%s(%s) job=%s place=%s players=%d brainrots=%d source=%s",
        tostring(payload.user),
        tostring(payload.userid),
        tostring(payload.jobid),
        tostring(payload.placeid),
        tonumber(payload.playerCount) or 0,
        #brainrots,
        tostring(source)
    ))

    if payload.jobid ~= "" then
        markServerBlockedForever(payload.jobid)
    end

    local response, requestErr = sendJoinerRequest("POST", "/api/joiner/report", payload)
    if not response then
        joinerDebugWarn("report failed: request error=" .. tostring(requestErr))
        return false, requestErr or "request_failed"
    end

    local statusCode = getResponseStatusCode(response)
    local decoded = decodeResponseBody(response)
    joinerDebugWarn("report <- status=" .. tostring(statusCode) .. " body=" .. safeJsonEncode(decoded or (response.Body or response.body or "")))

    if type(decoded) == "table" and decoded.ok == true then
        lastJoinerReportAt = now
        return true, nil
    end

    return false, "non_ok_response"
end

local function attemptTeleportToServer(placeId, serverJobId, sourceLabel)
    placeId = tonumber(placeId) or TargetPlaceId
    serverJobId = tostring(serverJobId or "")
    if serverJobId == "" then
        return false
    end

    if ENABLE_SHARED_CROSS_ACCOUNT_BLOCKLIST then
        refreshSharedIgnoreMapIfNeeded(true)
        if not isSharedServerClaimOwnedBySelf(serverJobId) then
            joinerDebugWarn("teleport aborted (claim lost) target=" .. serverJobId)
            markServerBlocked(serverJobId, math.max(1, tonumber(SHARED_SERVER_BLOCK_SECONDS) or JOINER_SERVER_BLOCK_SECONDS))
            return false
        end
    end

    -- Mark before teleport so this target is never retried even if call path fails later.
    markServerBlockedForever(serverJobId)
    lastTeleportTargetJobId = serverJobId

    joinerDebugWarn("teleport -> source=" .. tostring(sourceLabel or "unknown") .. " target=" .. serverJobId .. " place=" .. tostring(placeId))

    pushLiveStatusToRam(true, false)

    lastHopActionAt = tick()
    local ok, err = pcall(function()
        TeleportService:TeleportToPlaceInstance(placeId, serverJobId, LocalPlayer)
    end)

    if not ok then
        joinerDebugWarn("teleport call failed target=" .. serverJobId .. " err=" .. tostring(err))
        return false
    end

    return true
end

-- ------------------------------
-- Existing cache + cursor logic
-- ------------------------------
local function encodeAndPersist(jsonString)
    local okDecode, decoded = pcall(function()
        return HttpService:JSONDecode(jsonString)
    end)
    if not okDecode or type(decoded) ~= "table" then
        return nil, "decode_failed"
    end

    decoded.gameId = TargetPlaceId
    decoded.excludeFullGames = currentExcludeFullGames
    if type(decoded.data) == "table" then
        shuffleListInPlace(decoded.data)
    end
    local okEncode, encoded = pcall(function()
        return HttpService:JSONEncode(decoded)
    end)
    if not okEncode then
        return nil, "encode_failed"
    end
    writefile(CACHE_FILE, encoded)
    return decoded, nil
end

local function fetchServerPage(cursor, excludeFullGames)
    local suffix = cursor and ("&cursor=" .. tostring(cursor)) or ""
    local url = API .. "&excludeFullGames=" .. tostring(excludeFullGames) .. suffix

    local okFetch, responseOrErr = pcall(function()
        return game:HttpGet(url)
    end)

    if okFetch and type(responseOrErr) == "string" and responseOrErr ~= "" then
        return responseOrErr, nil
    end

    local errText = tostring(responseOrErr)
    return nil, errText ~= "" and errText or "fetch_failed"
end
local function nextCursor(cursor)
    return fetchServerPage(cursor, currentExcludeFullGames)
end

local function persistCacheTable(cacheTable)
    cacheTable.excludeFullGames = currentExcludeFullGames
    local okEncode, encoded = pcall(function()
        return HttpService:JSONEncode(cacheTable)
    end)
    if okEncode then
        writefile(CACHE_FILE, encoded)
    end
end

local function rebuildCacheForMode(excludeFullGames, cursor)
    currentExcludeFullGames = excludeFullGames
    local raw, fetchErr = fetchServerPage(cursor, currentExcludeFullGames)
    if type(raw) ~= "string" or raw == "" then
        return nil, fetchErr or "fetch_failed"
    end
    return encodeAndPersist(raw)
end

local function startTeleport()
    local cache = nil
    local cacheErr = nil

    while not cache do
        local rawCache = nil
        local readErr = nil
        local okRead, readResult = pcall(function()
            return readfile(CACHE_FILE)
        end)
        if okRead and type(readResult) == "string" and readResult ~= "" then
            rawCache = readResult
        else
            readErr = readResult
        end

        if rawCache then
            cache, cacheErr = encodeAndPersist(rawCache)
        end

        if not cache then
            if cacheErr then
                warn("[Joiner Hopper] Cache decode failed: " .. tostring(cacheErr))
            elseif readErr then
                warn("[Joiner Hopper] Cache read failed: " .. tostring(readErr))
            end

            local rebuilt, rebuildErr = rebuildCacheForMode(currentExcludeFullGames, nil)
            if type(rebuilt) == "table" and type(rebuilt.data) == "table" then
                cache = rebuilt
                break
            end

            warn("[Joiner Hopper] Cache rebuild failed, retrying: " .. tostring(rebuildErr))
            task.wait(2)
        end
    end

    local list = cache.data
    if type(list) ~= "table" then
        list = {}
        cache.data = list
    end

    local index = 1
    while true do
        if #list <= 0 then
            local rebuilt = nil

            if cache.nextPageCursor then
                local nextPageRaw, nextPageErr = nextCursor(cache.nextPageCursor)
                if type(nextPageRaw) == "string" and nextPageRaw ~= "" then
                    rebuilt = encodeAndPersist(nextPageRaw)
                else
                    warn("[Joiner Hopper] Next page fetch failed: " .. tostring(nextPageErr))
                    task.wait(2)
                end
            elseif currentExcludeFullGames then
                joinerDebugWarn("fallback mode -> include full servers")
                rebuilt = rebuildCacheForMode(false, nil)
            else
                currentExcludeFullGames = ExcludefullServers
                rebuilt = rebuildCacheForMode(currentExcludeFullGames, nil)
            end

            if type(rebuilt) == "table" and type(rebuilt.data) == "table" then
                cache = rebuilt
                list = cache.data
                index = 1
            else
                warn("[Joiner Hopper] Server list refresh failed, retrying...")
                task.wait(2)
            end
        end

        local item = list[index]
        if type(item) ~= "table" then
            table.remove(list, index)
            persistCacheTable(cache)
            task.wait(IterationSpeed)
        else
            local jobId = tostring(item.id or "")
            table.remove(list, index)
            persistCacheTable(cache)

            if jobId ~= "" and jobId ~= tostring(game.JobId) and tryClaimServerForHop(jobId) then
                if SaveTeleportAttempts then
                    appendfile(ATTEMPTS_FILE, jobId .. "\n")
                end

                attemptTeleportToServer(TargetPlaceId, jobId, "client_fallback")
                task.wait(IterationSpeed)
            end
        end
    end
end

local function setMainPage()
    while true do
        local rebuilt = rebuildCacheForMode(currentExcludeFullGames, nil)
        if rebuilt then
            startTeleport()
            return
        end

        local mainPage, fetchErr = fetchServerPage(nil, currentExcludeFullGames)
        if type(mainPage) == "string" and mainPage ~= "" then
            writefile(CACHE_FILE, mainPage)
            startTeleport()
            return
        end

        warn("[Joiner Hopper] Initial server list fetch failed: " .. tostring(fetchErr))
        task.wait(2)
    end
end

-- ------------------------------
-- UI cleanup (optional)
-- ------------------------------
if RemoveErrorPrompts then
    clearRobloxLoadingUi()
    task.spawn(function()
        while task.wait(0.5) do
            clearRobloxLoadingUi()
        end
    end)
end

-- ------------------------------
-- Init
-- ------------------------------
loadIgnoreList()
markServerBlockedForever(tostring(game.JobId or ""))

task.spawn(function()
    while task.wait(JOINER_REPORT_INTERVAL) do
        reportCurrentServerToJoiner(true)
    end
end)

task.spawn(function()
    if not RAM_STATUS_PUSH_ENABLED then
        return
    end

    pushLiveStatusToRam(true)

    while task.wait(math.max(0.25, tonumber(RAM_STATUS_PUSH_INTERVAL) or 1)) do
        pushLiveStatusToRam(false)
    end
end)

task.spawn(function()
    if not STALL_RECOVERY_ENABLED then
        return
    end

    while task.wait(math.max(0.1, tonumber(STALL_RECOVERY_CHECK_INTERVAL) or 0.5)) do
        if JOINER_REPORT_ONLY_MODE then
            break
        end

        local nowTick = tick()
        if profileSessionHoldUntil > nowTick then
            lastHopActionAt = nowTick
        else
            local stallAfter = math.max(2, tonumber(STALL_RECOVERY_SECONDS) or 6)
            local cooldown = math.max(1, tonumber(STALL_RECOVERY_COOLDOWN) or 3)

            if (nowTick - lastHopActionAt) >= stallAfter and (nowTick - lastStallRecoveryAt) >= cooldown then
                lastStallRecoveryAt = nowTick
                lastHopActionAt = nowTick
                markCurrentServerAsBad("stall_recovery")
                pushLiveStatusToRam(true, false)
                queueAutoRejoin("stall_recovery", true, true)
            end
        end
    end
end)

task.spawn(function()
    if not SIMPLE_REJOIN_FALLBACK_ENABLED then
        return
    end

    while task.wait(math.max(0.1, tonumber(SIMPLE_REJOIN_HEARTBEAT_INTERVAL) or 0.5)) do
        if JOINER_REPORT_ONLY_MODE then
            break
        end

        if profileSessionHoldUntil > tick() then
            simpleFallbackHeartbeat = 0
        else
            simpleFallbackHeartbeat = simpleFallbackHeartbeat + 1
            local recentError = (tick() - simpleFallbackLastErrorAt) <= math.max(2, tonumber(SIMPLE_REJOIN_ERROR_TTL_SECONDS) or 20)
            local triggerEvery = math.max(1, tonumber(SIMPLE_REJOIN_HEARTBEAT_TRIGGER) or 10)
            if recentError and (simpleFallbackHeartbeat % triggerEvery) == 0 then
                triggerSimpleFallbackTeleport("heartbeat:" .. tostring(simpleFallbackLastErrorText))
            end
        end
    end
end)

task.spawn(function()
    if not NO_BASE_WATCHDOG then
        return
    end

    local misses = 0
    local fullHits = 0
    local lastTriggeredAt = 0
    while task.wait(math.max(0.1, tonumber(NO_BASE_CHECK_INTERVAL) or 0.5)) do
        if profileSessionHoldUntil > tick() then
            misses = 0
            fullHits = 0
        else
            if (tick() - scriptStartedAt) < math.max(2, tonumber(NO_BASE_GRACE_SECONDS) or 12) then
                misses = 0
                fullHits = 0
            else
                local hasPlot, totalPlots, occupiedPlots = getLocalPlotOwnershipState()
                if hasPlot == true then
                    misses = 0
                    fullHits = 0
                elseif hasPlot == false then
                    misses = misses + 1
                    if (tonumber(totalPlots) or 0) > 0 and (tonumber(occupiedPlots) or 0) >= (tonumber(totalPlots) or 0) then
                        fullHits = fullHits + 1
                    else
                        fullHits = 0
                    end

                    local missLimit = math.max(1, tonumber(NO_BASE_MISS_LIMIT) or 8)
                    local fullLimit = math.max(1, tonumber(NO_BASE_FULL_SERVER_HIT_LIMIT) or 2)
                    if (misses >= missLimit or fullHits >= fullLimit) and (tick() - lastTriggeredAt) >= 2 then
                        lastTriggeredAt = tick()
                        misses = 0
                        fullHits = 0
                        markCurrentServerAsBad("no_base_watchdog")
                        startImmediateRejoinBurst("no_base_watchdog")
                        queueAutoRejoin("no_base_watchdog", true, true)
                    end
                end
            end
        end
    end
end)

if JOINER_REPORT_ONLY_MODE then
    joinerDebugWarn("REPORT-ONLY mode active (teleport disabled)")
    reportCurrentServerToJoiner(true)
else
    reportCurrentServerToJoiner(true)

    if isfile(CACHE_FILE) then
        local okDecode, cache = pcall(function()
            return HttpService:JSONDecode(readfile(CACHE_FILE))
        end)

        if okDecode and type(cache) == "table" then
            if tonumber(cache.gameId) ~= tonumber(TargetPlaceId) then
                setMainPage()
            else
                if cache.excludeFullGames ~= nil and tostring(cache.excludeFullGames) == "false" then
                    currentExcludeFullGames = false
                else
                    currentExcludeFullGames = ExcludefullServers
                end

                if type(cache.data) == "table" and #cache.data >= 1 then
                    startTeleport()
                elseif cache.nextPageCursor then
                    local rebuilt = encodeAndPersist(nextCursor(cache.nextPageCursor))
                    if rebuilt then
                        startTeleport()
                    else
                        setMainPage()
                    end
                else
                    setMainPage()
                end
            end
        else
            setMainPage()
        end
    else
        setMainPage()
    end
end
