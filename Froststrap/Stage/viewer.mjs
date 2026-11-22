import {
  API,
  Authentication,
  CFrame,
  FLAGS,
  Outfit,
  OutfitRenderer,
  RBXRenderer,
} from "roavatar-renderer";

FLAGS.ONLINE_ASSETS = true;
FLAGS.USE_WORKERS = false;
FLAGS.API_REQUEST_RETRY = false;
FLAGS.AUDIO_ENABLED = false;
FLAGS.USE_POST_PROCESSING = true;
FLAGS.POST_PROCESSING_IS_DOUBLE_SIZE = false;
FLAGS.FETCH_FUNC = fetchFromHost;
FLAGS.IMAGE_FUNC = imageFromHost;

const auth = new Authentication();
const waiters = new Map();
let sequence = 1;
let renderer;
let itemRoot;
let ready = false;
let queued = null;
let animation = "idle:0";
let playing = true;
let speed = 1;
let last = performance.now();
let cameraHeld = false;

function post(message) {
  window.chrome.webview.postMessage(message);
}

window.chrome.webview.addEventListener("message", (event) => {
  const data = event.data;
  if (!data || typeof data !== "object")
    return;
  if (data.kind === "file") {
    const waiter = waiters.get(data.id);
    if (!waiter)
      return;
    waiters.delete(data.id);
    if (data.error)
      waiter.reject(new Error(data.error));
    else
      waiter.resolve(data);
    return;
  }
  if (data.kind === "outfit" || data.kind === "item" || data.kind === "motion")
    void apply(data);
});

function requestFile(url, headers) {
  const id = sequence++;
  return new Promise((resolve, reject) => {
    waiters.set(id, { resolve, reject });
    post({ kind: "fetch", id, url, headers });
    setTimeout(() => {
      if (!waiters.has(id))
        return;
      waiters.delete(id);
      reject(new Error("The file took too long."));
    }, 45000);
  });
}

function headerList(input, init) {
  const source = init?.headers ?? (input instanceof Request ? input.headers : undefined);
  const headers = {};
  if (!source)
    return headers;
  const list = source instanceof Headers ? source : new Headers(source);
  const format = list.get("Roblox-AssetFormat");
  if (format)
    headers["Roblox-AssetFormat"] = format;
  return headers;
}

async function fetchFromHost(input, init) {
  const url = typeof input === "string" ? input : input instanceof URL ? input.toString() : input.url;
  if (url.startsWith("https://spectre.stage/"))
    return fetch(url);

  const file = await requestFile(url, headerList(input, init));
  if (file.asLocation)
    return Response.json({ locations: [{ location: file.local }] });

  const bytes = await fetch(file.local);
  return new Response(await bytes.arrayBuffer(), {
    status: file.status || 200,
    headers: { "content-type": file.type || "application/octet-stream" },
  });
}

async function imageFromHost(input) {
  if (!input || input.startsWith("data:") || input.startsWith("https://spectre.stage/"))
    return input;
  try {
    const file = await requestFile(input, {});
    return file.local;
  } catch {
    return input;
  }
}

function clearItem() {
  if (!itemRoot)
    return;
  const accessory = itemRoot.FindFirstChildOfClass?.("Accessory");
  if (accessory)
    RBXRenderer.removeInstance(accessory);
  itemRoot.Destroy?.();
  itemRoot = null;
}

function dropOutfit() {
  if (!renderer)
    return;
  renderer.doAddInstance = false;
  renderer.doCameraUpdateOnLoad = false;
  renderer.destroy();
  renderer = null;
}

async function showOutfit(data) {
  cameraHeld = false;
  clearItem();
  const outfit = new Outfit();
  outfit.fromJson(data.outfit);
  if (!renderer) {
    const created = new OutfitRenderer(auth, outfit);
    created.onSuccess.Connect(() => {
      if (renderer !== created)
        return;
      frameCamera(created.currentRig);
      post({ kind: "status", state: "shown" });
      if (animation)
        void created.setMainAnimation(animation);
    });
    created.onError.Connect(() => {
      if (renderer === created)
        post({ kind: "status", state: "failed", message: "The avatar could not be drawn." });
    });
    created.onRenderSuccess.Connect(() => {
      frameCamera(created.currentRig);
      RBXRenderer.animateAll(false);
      post({ kind: "status", state: "shown" });
    });
    renderer = created;
    return;
  }
  renderer.setOutfit(outfit);
}

async function showItem(data) {
  dropOutfit();
  clearItem();
  const model = await API.Asset.GetRBX(`rbxassetid://${data.assetId}`, {
    "Roblox-AssetFormat": "avatar_meshpart_accessory",
  }).catch(() => null);
  if (!model || model instanceof Response || !model.generateTree) {
    post({ kind: "status", state: "failed", message: "This item has no 3D model." });
    return;
  }
  const root = model.generateTree();
  const shown = root.FindFirstChildOfClass("Accessory") || root.FindFirstChildOfClass("MeshPart") || root;
  itemRoot = root;
  RBXRenderer.addInstance(shown, auth);
  RBXRenderer.animateAll(false);
  post({ kind: "status", state: "shown" });
}

async function apply(data) {
  if (!ready) {
    queued = data;
    return;
  }
  try {
    if (typeof data.speed === "number")
      speed = data.speed;
    if (typeof data.playing === "boolean")
      playing = data.playing;
    if (data.animation)
      animation = data.animation;
    if (data.kind === "motion") {
      if (renderer && data.animation)
        await renderer.setMainAnimation(data.animation);
      return;
    }
    if (data.kind === "item") {
      await showItem(data);
      return;
    }
    if (data.kind === "outfit")
      await showOutfit(data);
  } catch {
    post({ kind: "status", state: "failed", message: "The avatar could not be drawn." });
  }
}

const viewerFieldOfView = 30;

function boundsOf(rig) {
  let minX = Infinity, minY = Infinity, minZ = Infinity;
  let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
  let found = false;
  const visit = (instance) => {
    if (instance.IsA?.("BasePart")) {
      const size = instance.Prop("Size");
      const cf = instance.Prop("CFrame");
      if (size && cf && Math.max(size.X, size.Y, size.Z) < 30) {
        for (const x of [-size.X / 2, size.X / 2]) {
          for (const y of [-size.Y / 2, size.Y / 2]) {
            for (const z of [-size.Z / 2, size.Z / 2]) {
              const point = cf.multiply(new CFrame(x, y, z)).Position;
              minX = Math.min(minX, point[0]);
              maxX = Math.max(maxX, point[0]);
              minY = Math.min(minY, point[1]);
              maxY = Math.max(maxY, point[1]);
              minZ = Math.min(minZ, point[2]);
              maxZ = Math.max(maxZ, point[2]);
              found = true;
            }
          }
        }
      }
    }
    for (const child of instance.GetChildren?.() ?? [])
      visit(child);
  };
  visit(rig);
  return found ? { minX, minY, minZ, maxX, maxY, maxZ } : null;
}

function frameCamera(rig) {
  const controls = RBXRenderer.getRendererControls();
  const scene = RBXRenderer.firstScene;
  const camera = scene.camera;
  if (!controls || cameraHeld)
    return;
  const bounds = rig ? boundsOf(rig) : null;
  const targetX = bounds ? (bounds.minX + bounds.maxX) / 2 : controls.target.x;
  const targetY = bounds ? (bounds.minY + bounds.maxY) / 2 : controls.target.y;
  const targetZ = bounds ? (bounds.minZ + bounds.maxZ) / 2 : controls.target.z;
  const height = bounds ? Math.max(1, bounds.maxY - bounds.minY) : 5.2;
  const width = bounds ? Math.max(1, bounds.maxX - bounds.minX, bounds.maxZ - bounds.minZ) : 2.4;
  const aspect = camera.aspect > 0 ? camera.aspect : 1;
  const tangent = Math.tan((viewerFieldOfView * Math.PI) / 360);
  const distance = Math.max(height / 2 / tangent, width / 2 / (tangent * aspect)) / 0.86;
  const [x, y, z] = scene.lookAwayVector;
  const length = Math.hypot(x, y, z) || 1;
  camera.fov = viewerFieldOfView;
  controls.target.set(targetX, targetY, targetZ);
  camera.position.set(targetX + x / length * distance, targetY + y / length * distance, targetZ + z / length * distance);
  camera.lookAt(controls.target);
  camera.updateProjectionMatrix();
  controls.update();
}

function frame() {
  const now = performance.now();
  const step = Math.min(0.05, (now - last) / 1000);
  last = now;
  if (playing && renderer)
    renderer.animateOnce(step * speed);
  RBXRenderer.animateAll(false);
}

async function boot() {
  const canvas = document.createElement("canvas");
  RBXRenderer.createLoadingIcon = false;
  RBXRenderer.firstScene.shadowResolution = [1024, 1024];
  RBXRenderer.createContainer();
  await RBXRenderer.boilerplateSetup();
  RBXRenderer.create(canvas);
  const element = RBXRenderer.getRendererElement();
  element.style.width = "100%";
  element.style.height = "100%";
  document.body.append(element);
  RBXRenderer.setupScene("WellLit", "#0c0c0e");
  RBXRenderer.setupControls();
  const controls = RBXRenderer.getRendererControls();
  if (controls) {
    controls.maxDistance = 120;
    controls.addEventListener("start", () => { cameraHeld = true; });
    controls.addEventListener("change", () => RBXRenderer.animateAll(false));
  }
  frameCamera();
  const resize = () => {
    const camera = RBXRenderer.firstScene.camera;
    RBXRenderer.setRendererSize(innerWidth, innerHeight);
    camera.aspect = innerWidth / Math.max(1, innerHeight);
    camera.updateProjectionMatrix();
    frameCamera(renderer?.currentRig);
    RBXRenderer.animateAll(false);
  };
  addEventListener("resize", resize);
  resize();
  ready = true;
  post({ kind: "status", state: "ready" });
  if (queued) {
    const pending = queued;
    queued = null;
    await apply(pending);
  }
  setInterval(frame, 16);
}

boot().catch((error) => post({ kind: "status", state: "failed", message: String(error?.message || error) }));
