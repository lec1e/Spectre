import {
  API,
  AnimatorWrapper,
  Authentication,
  CFrame,
  FLAGS,
  Outfit,
  OutfitRenderer,
  RBXRenderer,
  getThumbnailCamera,
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
let shownMesh;
let ready = false;
let queued = null;
let animation = "idle:0";
let playing = true;
let speed = 1;
let last = performance.now();
let cameraHeld = false;
let framingItem = false;
let framingMood = false;
let meshShot = null;

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
  if (data.kind === "outfit" || data.kind === "item" || data.kind === "model" || data.kind === "mesh" || data.kind === "motion")
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

function setGround(visible) {
  const scene = RBXRenderer.firstScene;
  if (scene?.plane) scene.plane.visible = visible;
  if (scene?.shadowPlane) scene.shadowPlane.visible = visible;
}

function clearMesh() {
  if (!shownMesh)
    return;
  RBXRenderer.firstScene.scene.remove(shownMesh);
  shownMesh.traverse((child) => {
    child.geometry?.dispose();
    const materials = Array.isArray(child.material) ? child.material : [child.material];
    for (const material of materials) {
      material?.map?.dispose();
      material?.dispose();
    }
  });
  shownMesh = null;
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
  framingItem = false;
  framingMood = typeof animation === "string" && animation.startsWith("mood.");
  setGround(true);
  clearMesh();
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
        void playMotion(animation);
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
  cameraHeld = false;
  framingItem = true;
  setGround(true);
  dropOutfit();
  clearMesh();
  clearItem();
  const model = await API.Asset.GetRBX(`rbxassetid://${data.assetId}`, {
    "Roblox-AssetFormat": "avatar_meshpart_accessory",
  }).catch(() => null);
  if (!model || model instanceof Response || !model.generateTree) {
    framingItem = false;
    post({ kind: "status", state: "failed", message: "This item has no 3D model." });
    return;
  }
  const root = model.generateTree();
  const accessory = root.FindFirstChildOfClass("Accessory");
  if (!accessory) {
    root.Destroy?.();
    framingItem = false;
    post({ kind: "status", state: "failed", message: "This item has no 3D model." });
    return;
  }
  itemRoot = root;
  RBXRenderer.addInstance(accessory, auth);
  frameAccessory(accessory);
  RBXRenderer.animateAll(false);
  post({ kind: "status", state: "shown" });
}

async function showModel(data) {
  cameraHeld = false;
  framingMood = false;
  framingItem = false;
  setGround(false);
  dropOutfit();
  clearMesh();
  clearItem();
  const model = await API.Asset.GetRBX(`rbxassetid://${data.assetId}`).catch(() => null);
  if (!model || model instanceof Response || !model.generateTree) {
    post({ kind: "status", state: "failed", message: "This model has no 3D preview." });
    return;
  }
  const root = model.generateTree();
  const shown = root.FindFirstChildOfClass("Model") || root.FindFirstChildOfClass("Accessory") || root.FindFirstChildOfClass("MeshPart") || root;
  itemRoot = root;
  RBXRenderer.addInstance(shown, auth);
  frameCamera(shown, 512);
  framingItem = true;
  RBXRenderer.animateAll(false);
  post({ kind: "status", state: "shown" });
}

function sceneMeshTypes() {
  const plane = RBXRenderer.firstScene?.plane;
  const position = plane?.geometry?.attributes?.position;
  const Object3D = plane ? Object.getPrototypeOf(plane.constructor.prototype)?.constructor : null;
  const geometryParent = plane ? Object.getPrototypeOf(plane.geometry.constructor.prototype)?.constructor : null;
  const BufferGeometry = geometryParent?.prototype?.setAttribute ? geometryParent : plane?.geometry?.constructor;
  if (!plane || !position || !Object3D || !BufferGeometry?.prototype?.setAttribute)
    return null;
  return {
    Group: Object3D,
    Mesh: plane.constructor,
    BufferGeometry,
    Attribute: position.constructor,
    Material: plane.material.constructor,
  };
}

async function showTexturedMesh(data) {
  cameraHeld = false;
  framingMood = false;
  framingItem = true;
  meshShot = data.shot || null;
  setGround(false);
  dropOutfit();
  clearItem();
  clearMesh();
  const text = typeof data.objText === "string" && data.objText.includes("v ")
    ? data.objText
    : await readObj(data.obj);
  const parsed = parseObj(text);
  if (parsed.faces.length === 0)
    throw new Error("This model has no 3D mesh.");
  const types = sceneMeshTypes();
  if (!types)
    throw new Error("The 3D view is not ready.");
  const paints = await texturePaints(data);
  const group = new types.Group();
  const buckets = new Map();
  for (const face of parsed.faces) {
    const key = face.material || "";
    const list = buckets.get(key) ?? [];
    list.push(face);
    buckets.set(key, list);
  }
  const fallback = paints.get("") || paints.values().next().value || null;
  for (const [name, faces] of buckets) {
    const position = [];
    const color = [];
    const paint = paints.get(name) || fallback;
    for (const face of faces) {
      for (const corner of [face.a, face.b, face.c]) {
        const point = parsed.positions[corner[0]];
        if (!point)
          continue;
        position.push(point[0], point[1], point[2]);
        const tex = parsed.uvs[corner[1]] || [0, 0];
        const sample = paint ? samplePaint(paint, tex[0], tex[1]) : [0.82, 0.82, 0.86];
        color.push(sample[0], sample[1], sample[2]);
      }
    }
    if (position.length < 9)
      continue;
    const geometry = new types.BufferGeometry();
    geometry.setAttribute("position", new types.Attribute(new Float32Array(position), 3));
    geometry.setAttribute("color", new types.Attribute(new Float32Array(color), 3));
    const material = new types.Material({ color: 0xffffff, vertexColors: true });
    material.side = 2;
    material.toneMapped = false;
    group.add(new types.Mesh(geometry, material));
  }
  if (group.children.length === 0)
    throw new Error("This model has no 3D mesh.");
  shownMesh = group;
  RBXRenderer.firstScene.scene.add(group);
  frameMesh(group, meshShot);
  RBXRenderer.animateAll(false);
  post({ kind: "status", state: "shown" });
}

async function readObj(address) {
  if (!address)
    throw new Error("The model file could not be opened.");
  const response = await fetch(address);
  if (!response.ok)
    throw new Error("The model file could not be opened.");
  return response.text();
}

async function texturePaints(data) {
  const paints = new Map();
  const urls = data.textures || {};
  const decoded = new Map();
  for (const [name, address] of Object.entries(urls)) {
    if (!address || decoded.has(address))
      continue;
    const paint = await decodePaint(address);
    if (paint)
      decoded.set(address, paint);
  }
  const assigned = materialMap(data.mtl || "", urls);
  for (const [name, address] of Object.entries(assigned)) {
    const paint = decoded.get(address);
    if (paint)
      paints.set(name, paint);
  }
  if (paints.size === 0) {
    const first = decoded.values().next().value;
    if (first)
      paints.set("", first);
  }
  return paints;
}

function decodePaint(address) {
  return new Promise((resolve) => {
    const image = new Image();
    image.onload = () => {
      const width = image.naturalWidth || image.width;
      const height = image.naturalHeight || image.height;
      if (width < 1 || height < 1) {
        resolve(null);
        return;
      }
      const canvas = document.createElement("canvas");
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext("2d", { willReadFrequently: true });
      context.drawImage(image, 0, 0, width, height);
      resolve({ pixels: context.getImageData(0, 0, width, height).data, width, height });
    };
    image.onerror = () => resolve(null);
    image.src = address;
  });
}

function samplePaint(paint, u, v) {
  const wrappedU = u - Math.floor(u);
  const wrappedV = v - Math.floor(v);
  const x = Math.min(paint.width - 1, Math.max(0, Math.floor(wrappedU * paint.width)));
  const y = Math.min(paint.height - 1, Math.max(0, Math.floor((1 - wrappedV) * paint.height)));
  const index = (y * paint.width + x) * 4;
  return [paint.pixels[index] / 255, paint.pixels[index + 1] / 255, paint.pixels[index + 2] / 255];
}

function parseObj(text) {
  const positions = [];
  const uvs = [];
  const faces = [];
  let material = "";
  for (const raw of text.split("\n")) {
    const line = raw.trim();
    if (line.startsWith("usemtl "))
      material = line.slice(7).trim();
    else if (line.startsWith("v ")) {
      const parts = line.split(/\s+/);
      positions.push([Number(parts[1]), Number(parts[2]), Number(parts[3])]);
    } else if (line.startsWith("vt ")) {
      const parts = line.split(/\s+/);
      uvs.push([Number(parts[1]), Number(parts[2])]);
    } else if (line.startsWith("f ")) {
      const corners = line.split(/\s+/).slice(1).map((part) => {
        const bits = part.split("/");
        const position = Number(bits[0]);
        const uv = bits[1] ? Number(bits[1]) : 0;
        return [
          position < 0 ? positions.length + position : position - 1,
          uv < 0 ? uvs.length + uv : uv - 1,
        ];
      });
      for (let index = 1; index < corners.length - 1; index++)
        faces.push({ a: corners[0], b: corners[index], c: corners[index + 1], material });
    }
  }
  return { positions, uvs, faces };
}

function materialMap(mtl, textures) {
  const map = {};
  let current = "";
  for (const raw of mtl.split("\n")) {
    const line = raw.trim();
    if (line.startsWith("newmtl "))
      current = line.slice(7).trim();
    else if (current && line.startsWith("map_Kd ")) {
      const token = line.slice(7).trim().split(/\s+/).pop();
      if (token && textures[token])
        map[current] = textures[token];
    }
  }
  if (Object.keys(map).length === 0) {
    const only = Object.values(textures)[0];
    if (only)
      map[""] = only;
  }
  return map;
}

function frameMesh(group, shot) {
  let minX = Infinity, minY = Infinity, minZ = Infinity;
  let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
  group.traverse?.((child) => {
    const geometry = child.geometry;
    if (!geometry?.computeBoundingBox)
      return;
    geometry.computeBoundingBox();
    const bounds = geometry.boundingBox;
    if (!bounds?.min || !bounds?.max)
      return;
    minX = Math.min(minX, bounds.min.x);
    minY = Math.min(minY, bounds.min.y);
    minZ = Math.min(minZ, bounds.min.z);
    maxX = Math.max(maxX, bounds.max.x);
    maxY = Math.max(maxY, bounds.max.y);
    maxZ = Math.max(maxZ, bounds.max.z);
  });
  if (!Number.isFinite(minX))
    return;
  const center = [(minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2];
  const size = [maxX - minX, maxY - minY, maxZ - minZ];
  const camera = RBXRenderer.firstScene.camera;
  const controls = RBXRenderer.getRendererControls();
  if (!controls)
    return;
  camera.fov = viewerFieldOfView;
  const aspect = camera.aspect > 0 ? camera.aspect : 1;
  const tangent = Math.tan((viewerFieldOfView * Math.PI) / 360);
  const distance = Math.max(size[1] / 2 / tangent, Math.max(size[0], size[2]) / 2 / (tangent * aspect)) / 0.82;
  let dx = 0.42, dy = 0.22, dz = 1;
  if (shot?.eye && shot?.target) {
    dx = shot.eye[0] - shot.target[0];
    dy = shot.eye[1] - shot.target[1];
    dz = shot.eye[2] - shot.target[2];
  }
  const length = Math.hypot(dx, dy, dz) || 1;
  const span = Math.max(distance, 0.4);
  controls.target.set(center[0], center[1], center[2]);
  camera.position.set(center[0] + dx / length * span, center[1] + dy / length * span, center[2] + dz / length * span);
  camera.lookAt(controls.target);
  camera.updateProjectionMatrix();
  controls.update();
  controls.maxDistance = Math.max(span * 8, 20);
}

function frameAccessory(accessory) {
  const thumbnail = getThumbnailCamera(accessory);
  const frame = thumbnail?.Prop?.("CFrame");
  const framedFieldOfView = Number(thumbnail?.PropOrDefault?.("FieldOfView", 70)) || 70;
  const handle = accessory.FindFirstChild("Handle");
  const position = handle?.Prop?.("Position");
  if (thumbnail && thumbnail !== accessory.FindFirstChild("ThumbnailCamera"))
    thumbnail.Destroy?.();
  if (!frame?.Position || !position)
    return;
  const eye = frame.Position;
  const target = [position.X ?? position[0], position.Y ?? position[1], position.Z ?? position[2]];
  placeCamera(eye, target, framedFieldOfView);
}

function placeCamera(eye, target, framedFieldOfView) {
  const controls = RBXRenderer.getRendererControls();
  const camera = RBXRenderer.firstScene.camera;
  if (!controls)
    return;
  const scale = Math.tan((framedFieldOfView * Math.PI) / 360) / Math.tan((viewerFieldOfView * Math.PI) / 360);
  controls.target.set(target[0], target[1], target[2]);
  camera.fov = viewerFieldOfView;
  camera.position.set(
    target[0] + (eye[0] - target[0]) * scale,
    target[1] + (eye[1] - target[1]) * scale,
    target[2] + (eye[2] - target[2]) * scale,
  );
  camera.lookAt(controls.target);
  camera.updateProjectionMatrix();
  controls.update();
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
        await playMotion(data.animation);
      return;
    }
    if (data.kind === "item") {
      await showItem(data);
      return;
    }
    if (data.kind === "model") {
      await showModel(data);
      return;
    }
    if (data.kind === "mesh") {
      try {
        await showTexturedMesh(data);
      } catch (error) {
        post({ kind: "status", state: "failed", message: error?.message || "This model has no 3D preview." });
      }
      return;
    }
    if (data.kind === "outfit")
      await showOutfit(data);
  } catch {
    post({ kind: "status", state: "failed", message: "The avatar could not be drawn." });
  }
}

const viewerFieldOfView = 30;

function boundsOf(rig, limit = 30) {
  let minX = Infinity, minY = Infinity, minZ = Infinity;
  let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
  let found = false;
  const visit = (instance) => {
    if (instance.IsA?.("BasePart")) {
      const size = instance.Prop("Size");
      const cf = instance.Prop("CFrame");
      if (size && cf && Math.max(size.X, size.Y, size.Z) < limit) {
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

async function playMotion(name) {
  if (!renderer || !name)
    return;
  const mood = name.startsWith("mood.");
  const wasMood = framingMood;
  framingMood = mood;
  if (name.startsWith("set.")) {
    framingMood = false;
    const parts = name.split(".");
    const humanoid = renderer.currentRig?.FindFirstChildOfClass("Humanoid");
    const animator = humanoid?.FindFirstChildOfClass("Animator");
    if (!animator)
      return;
    const wrapper = new AnimatorWrapper(animator);
    const id = BigInt(parts[2]);
    const slot = parts[1];
    try {
      await wrapper.loadAvatarAnimation(id, false, true);
      const names = Object.keys(wrapper.data?.animationSet ?? {});
      const match = names.find((key) => key.toLowerCase() === slot && wrapper.data.animationSet[key]?.length);
      if (match)
        wrapper.playAnimation(match);
      else {
        await wrapper.loadAvatarAnimation(id, true, true);
        wrapper.playAnimation(`emote.${parts[2]}`);
      }
    } catch {
      post({ kind: "status", state: "failed", message: "That animation could not be played." });
    }
    if (wasMood && !cameraHeld)
      frameCamera(renderer.currentRig);
    return;
  }
  if (!mood) {
    await renderer.setMainAnimation(name);
    if (wasMood && !cameraHeld)
      frameCamera(renderer.currentRig);
    return;
  }

  const humanoid = renderer.currentRig?.FindFirstChildOfClass("Humanoid");
  const animator = humanoid?.FindFirstChildOfClass("Animator");
  if (!animator)
    return;
  const wrapper = new AnimatorWrapper(animator);
  const loaded = await wrapper.loadAvatarAnimation(BigInt(name.slice(5)), false, true);
  if (loaded instanceof Response) {
    post({ kind: "status", state: "failed", message: "That mood could not be loaded." });
    return;
  }
  wrapper.stopMoodAnimation();
  wrapper.playAnimation("idle:0");
  wrapper.playAnimation("mood", "mood");
  frameHead(renderer.currentRig);
}

function frameHead(rig) {
  const controls = RBXRenderer.getRendererControls();
  const scene = RBXRenderer.firstScene;
  const camera = scene.camera;
  const head = rig?.FindFirstChild?.("Head");
  const size = head?.Prop?.("Size");
  const frame = head?.Prop?.("CFrame");
  if (!controls || !camera || !size || !frame?.Position || cameraHeld)
    return;
  const target = frame.Position;
  const height = Math.max(size.Y * 4.5, 3);
  const width = Math.max(size.X * 3.2, 2);
  const aspect = camera.aspect > 0 ? camera.aspect : 1;
  const tangent = Math.tan((viewerFieldOfView * Math.PI) / 360);
  const distance = Math.max(height / 2 / tangent, width / 2 / (tangent * aspect)) / 0.86;
  const [x, y, z] = scene.lookAwayVector;
  const length = Math.hypot(x, y, z) || 1;
  camera.fov = viewerFieldOfView;
  controls.target.set(target[0], target[1], target[2]);
  camera.position.set(target[0] + x / length * distance, target[1] + y / length * distance, target[2] + z / length * distance);
  camera.lookAt(controls.target);
  camera.updateProjectionMatrix();
  controls.update();
}

function frameCamera(rig, limit = 30) {
  const controls = RBXRenderer.getRendererControls();
  const scene = RBXRenderer.firstScene;
  const camera = scene.camera;
  if (!controls || cameraHeld || framingItem || framingMood)
    return;
  const bounds = rig ? boundsOf(rig, limit) : null;
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
    if (shownMesh && !cameraHeld)
      frameMesh(shownMesh, meshShot);
    else
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
