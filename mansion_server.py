#!/usr/bin/env python3
"""
Flask wrapper around MANSION for Unity SceneOrchestrator.

Exposes a single endpoint:
    POST /generate
    Body: { "requirement": "...", "floors": 3 }
    Returns: { "building_name": "gen_xxx", "floors": { "floor_1.json": {...}, ... } }

Usage:
    source ~/miniconda3/bin/activate mansion
    export OPENAI_API_KEY="your-key"
    python mansion_server.py

The server will listen on http://localhost:5050.
Unity's SceneOrchestrator POSTs to this endpoint.
"""

import glob
import json
import os
import sys
import traceback
from pathlib import Path

from flask import Flask, jsonify, request

# ── Configuration ────────────────────────────────────────────────
MANSION_DIR = os.path.expanduser(
    "~/Desktop/Projects/1111具身智能/仿真测试/MANSION"
)
OUTPUT_BASE = os.path.join(MANSION_DIR, "llm_planning_output_mac_json")

# Add MANSION to path so we can import its modules
if MANSION_DIR not in sys.path:
    sys.path.insert(0, MANSION_DIR)

app = Flask(__name__)


@app.route("/generate", methods=["POST"])
def generate():
    """Generate a building from a text requirement."""
    data = request.get_json(force=True)
    requirement = data.get("requirement", "office building")
    floors_count = data.get("floors", 3)

    # Unique output name
    import time
    output_name = f"gen_{int(time.time())}"
    output_dir = os.path.join(OUTPUT_BASE, output_name)

    try:
        from mansion.scripts.run_portable_full_pipeline import make_config, run_full_pipeline

        cfg = make_config(
            requirement=requirement,
            floors=floors_count,
            area=450,
            llm_provider="openai",
            output_dir="llm_planning_output_mac_json",
            generate_image=False,
            include_small_objects=False,
            clean_output=False,
        )

        run_full_pipeline(cfg, run_name_override=output_name)

    except Exception as e:
        traceback.print_exc()
        return jsonify({"error": str(e)}), 500

    # Collect generated floor JSONs
    if not os.path.isdir(output_dir):
        # Try looking one level deeper (some MANSION configs nest output)
        candidates = glob.glob(os.path.join(OUTPUT_BASE, output_name, "*"))
        for c in candidates:
            if os.path.isdir(c) and glob.glob(os.path.join(c, "floor_*.json")):
                output_dir = c
                break

    floor_files = sorted(glob.glob(os.path.join(output_dir, "floor_*.json")))
    if not floor_files:
        return jsonify({
            "error": "No floor_*.json found after generation",
            "output_dir": output_dir,
        }), 500

    floors = {}
    for f in floor_files:
        key = os.path.basename(f).replace(".json", "")
        with open(f) as fh:
            floors[key] = json.load(fh)

    return jsonify({
        "building_name": output_name,
        "floors": floors,
    })


@app.route("/health", methods=["GET"])
def health():
    """Health check endpoint."""
    return jsonify({"status": "ok", "mansion_dir": MANSION_DIR})


if __name__ == "__main__":
    print(f"MANSION dir: {MANSION_DIR}")
    print(f"Output base: {OUTPUT_BASE}")
    print("Starting server on http://localhost:5050")
    app.run(host="0.0.0.0", port=5050, debug=False)
