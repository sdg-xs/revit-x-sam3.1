# Revit x SAM 3.1

A local SAM 3.1 workflow for Revit 2027.3. SAM runs in a Python process on the local machine; a Revit .NET 10 add-in sends view images to that process and presents detections for review.

## Workflow target

1. Open the SAM 3.1 ribbon panel and check local worker/model status.
2. Choose a prompt for assets to find in the active Revit view.
3. Send a view capture to the local SAM 3.1 worker and review its masks/boxes.
4. Match detections to loaded Revit families and approve proposed placements.
5. Place approved families with a mapped model position and rotation.

The current starter implements the ribbon command, local worker status, local process launch, and an image/prompt inference count preview. It does not create families yet: SAM provides image-space detections, and reliable Revit coordinates/rotation require a view-to-model mapping and a placement plane or host. The next milestone is that mapping and family review UI.

## Requirements

- Autodesk Revit 2027.3
- .NET 10 SDK
- Python 3.12+
- NVIDIA GPU with CUDA 12.6+ for the intended local GPU inference path
- Access to Meta's SAM 3 checkpoints on Hugging Face; the model weights are gated and must be downloaded by an authorized account

Meta's official SAM 3 repository documents the model setup and current SAM 3.1 checkpoints: [facebookresearch/sam3](https://github.com/facebookresearch/sam3). SAM's license and checkpoint terms apply.

## Local worker setup

1. Install Python 3.12. This machine currently has Python 3.14, which does not meet SAM 3's documented Python requirement.
2. Clone Meta's official `facebookresearch/sam3` repository outside this project and follow its current Windows-compatible installation instructions.
3. From the project root, create `worker/.venv` with Python 3.12 and install the runtime packages. Meta currently documents Python 3.12+, PyTorch 2.7+, and CUDA 12.6+; its setup example uses PyTorch 2.10 with CUDA 12.8.

   ```powershell
   py -3.12 -m venv worker/.venv
   worker/.venv/Scripts/python.exe -m pip install --upgrade pip
   worker/.venv/Scripts/python.exe -m pip install torch==2.10.0 torchvision --index-url https://download.pytorch.org/whl/cu128
   worker/.venv/Scripts/python.exe -m pip install -e C:/path/to/facebookresearch/sam3
   worker/.venv/Scripts/python.exe -m pip install -e worker
   ```

4. Request access to Meta's gated SAM 3 checkpoint and authenticate locally with Hugging Face. The first inference loads the checkpoint from the local Hugging Face cache; no Revit view image is sent to a hosted model service.
5. Configure the add-in with `scripts/configure-worker.ps1 -PythonExecutable <worker .venv python.exe> -SamRepository <official SAM 3 checkout>`. This saves paths in `%LOCALAPPDATA%\RevitSam31\settings.json`; it stores no credentials. Do not commit model weights.
6. The add-in can start the worker. To run it manually instead, activate `worker/.venv`, set `SAM3_REPO` to the official checkout, and run `python -m sam_worker` from `worker`. The worker binds only to `127.0.0.1:8767`.

The add-in can launch the configured Python executable. If the paths are unset, it reports setup instructions instead of attempting a cloud service.

## Build and install the add-in

Build `src/RevitSam.Addin/RevitSam.Addin.csproj` in Release. Set `RevitApiPath` if Revit is installed outside the default `C:\Program Files\Autodesk\Revit 2027` directory. Copy the build output and `RevitSam.addin` manifest to `%AppData%\Autodesk\Revit\Addins\2027`, then restart Revit.

The add-in communicates with the local Python worker over loopback HTTP.
