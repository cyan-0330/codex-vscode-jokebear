"""Read VS Code Codex metadata and bounded local rollout tails; never write Codex data."""
import argparse
import datetime
import json
import os
from pathlib import Path
import sqlite3
import sys
import time


def stamp(value):
    if value is None:
        return 0
    if isinstance(value, (int, float)):
        return int(value if value > 100000000000 else value * 1000)
    try:
        return int(datetime.datetime.fromisoformat(str(value).replace("Z", "+00:00")).timestamp() * 1000)
    except (ValueError, TypeError):
        return 0


def connect(path):
    conn = sqlite3.connect(path.as_uri() + "?mode=ro", uri=True, timeout=2)
    conn.row_factory = sqlite3.Row
    return conn


def find_database(root, pattern, table, required):
    """Find a compatible numbered Codex database without hard-coding a schema file."""
    paths = sorted(root.glob(pattern), key=lambda p: int(p.stem.rsplit('_', 1)[-1]) if p.stem.rsplit('_', 1)[-1].isdigit() else 0, reverse=True)
    for path in paths:
        conn = None
        try:
            conn = connect(path)
            columns = {row['name'] for row in conn.execute('PRAGMA table_info(' + table + ')')}
            if required <= columns:
                return conn, columns
        except sqlite3.Error:
            pass
        if conn is not None:
            conn.close()
    return None, set()


def rollout_snapshot(path):
    turns, plans = {}, {}
    current = None
    try:
        with Path(path).open("rb") as stream:
            stream.seek(0, 2)
            length = stream.tell()
            offset = max(0, length - 2 * 1024 * 1024)
            stream.seek(offset)
            lines = stream.read().splitlines()
            if offset:
                lines = lines[1:]
        for line in lines:
            try:
                event = json.loads(line)
                payload = event.get("payload", {})
                event_type = event.get("type")
                kind = payload.get("type")
                at = stamp(event.get("timestamp"))
                if event_type == "turn_context":
                    current = payload.get("turn_id")
                elif event_type == "event_msg" and kind in ("task_started", "task_complete", "turn_aborted"):
                    current = payload.get("turn_id") or current
                    if current:
                        state = {"task_started": "running", "task_complete": "completed", "turn_aborted": "interrupted"}[kind]
                        turns[current] = {"State": state, "UpdatedMs": at, "StartedMs": stamp(payload.get("started_at"))}
                if current and current in turns:
                    turns[current]["UpdatedMs"] = max(turns[current]["UpdatedMs"], at)
                if event_type == "response_item" and payload.get("name", "").split(".")[-1] == "update_plan" and current:
                    arguments = payload.get("arguments", payload.get("input", {}))
                    if isinstance(arguments, str):
                        arguments = json.loads(arguments)
                    plan = arguments.get("plan", []) if isinstance(arguments, dict) else []
                    if plan and all(isinstance(step, dict) for step in plan):
                        plans[current] = (sum(step.get("status") == "completed" for step in plan), len(plan))
            except (ValueError, TypeError, AttributeError):
                continue
    except (OSError, TypeError):
        pass
    return turns, plans


def load_projects(root, now_ms=None):
    now_ms = now_ms or int(time.time() * 1000)
    cutoff = now_ms - 5 * 86400000
    db, columns = find_database(root, 'state_*.sqlite', 'threads', {'id', 'cwd', 'title', 'updated_at', 'source', 'rollout_path'})
    if db is None:
        raise sqlite3.OperationalError('No compatible VS Code conversation database; open a conversation in VS Code first')
    with db:
        # Source emitted by this VS Code extension; excludes CLI, App, subagents.
        updated_ms = 'updated_at_ms' if 'updated_at_ms' in columns else 'updated_at * 1000'
        threads = db.execute("SELECT id,cwd,title,updated_at," + updated_ms + " AS updated_at_ms,source,rollout_path FROM threads WHERE updated_at>=? AND source='vscode' ORDER BY updated_at DESC LIMIT 300", (cutoff // 1000,)).fetchall()
    db.close()
    projects = {}
    history, _ = find_database(root, 'thread_history_*.sqlite', 'thread_turns', {'thread_id', 'turn_id', 'rollout_ordinal', 'status', 'started_at', 'completed_at'})
    try:
        for thread in threads:
            path = os.path.normpath(thread["cwd"]) if thread["cwd"] else ""
            if path.lower().startswith("\\\\?\\unc\\"):
                path = "\\\\" + path[8:]
            elif path.startswith("\\\\?\\"):
                path = path[4:]
            updated = thread["updated_at_ms"] or stamp(thread["updated_at"])
            if updated < cutoff:
                continue
            # One card per Codex conversation, even when working directories match.
            project = {"SessionId": thread["id"], "Path": path, "Name": thread["title"] or "Codex 对话", "UpdatedMs": updated, "Tasks": [], "PlanTotal": 0, "PlanDone": 0}
            projects[thread["id"]] = project
            turns = history.execute("SELECT turn_id,status,started_at,completed_at FROM thread_turns WHERE thread_id=? ORDER BY rollout_ordinal DESC LIMIT 80", (thread["id"],)).fetchall() if history else []
            snapshot, plans = rollout_snapshot(thread["rollout_path"])
            known = set()
            for turn in turns:
                known.add(turn["turn_id"])
                changed = stamp(turn["completed_at"]) or stamp(turn["started_at"])
                observed = snapshot.get(turn["turn_id"], {})
                changed = max(changed, observed.get("UpdatedMs", 0)) or updated
                if changed < cutoff:
                    continue
                raw = (turn["status"] or "").lower().replace("_", "")
                state = "completed" if raw == "completed" else "interrupted" if raw in ("interrupted", "cancelled") else "failed" if raw in ("failed", "error") else "running" if raw == "inprogress" else "pending"
                if observed.get("UpdatedMs", 0) >= (stamp(turn["completed_at"]) or stamp(turn["started_at"])) and not (state in ("completed", "failed", "interrupted") and observed.get("State") == "running"):
                    state = observed.get("State", state)
                project["Tasks"].append({"Id": thread["id"] + ":" + turn["turn_id"], "SessionId": thread["id"], "Title": (thread["title"] or "Codex 任务")[:100], "State": state, "StartedMs": stamp(turn["started_at"]), "UpdatedMs": changed})
                project["UpdatedMs"] = max(project["UpdatedMs"], changed)
            # Streaming lifecycle can arrive before the history DB flushes.
            for turn_id, observed in snapshot.items():
                if turn_id not in known and observed["UpdatedMs"] >= cutoff:
                    project["Tasks"].append({"Id": thread["id"] + ":" + turn_id, "SessionId": thread["id"], "Title": (thread["title"] or "Codex 任务")[:100], **observed})
                    project["UpdatedMs"] = max(project["UpdatedMs"], observed["UpdatedMs"])
            latest = max(project["Tasks"], key=lambda task: task["UpdatedMs"], default=None)
            if latest and latest["SessionId"] == thread["id"]:
                turn_id = latest["Id"][len(thread["id"]) + 1:]
                if turn_id in plans:
                    project["PlanDone"], project["PlanTotal"] = plans[turn_id]
                    project["PlanTaskId"] = latest["Id"]
    finally:
        if history:
            history.close()
    return {"Source": "vscode", "Unit": "conversation", "SessionIds": list(projects), "Projects": sorted(projects.values(), key=lambda p: p["UpdatedMs"], reverse=True), "Hidden": []}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--codex-dir", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try:
        payload = load_projects(Path(args.codex_dir).resolve())
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        temporary = output.with_suffix(".tmp")
        temporary.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
        os.replace(str(temporary), str(output))
    except (OSError, sqlite3.Error) as error:
        print("VS Code conversation import failed: " + type(error).__name__, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
