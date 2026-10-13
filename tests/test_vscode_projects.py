import datetime
import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest

module_path = Path(__file__).resolve().parents[1] / "scripts" / "vscode_projects.py"
spec = importlib.util.spec_from_file_location("vscode_projects", module_path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class VscodeImportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.now = 1791800000000
        with sqlite3.connect(str(self.root / "state_5.sqlite")) as db:
            db.execute("CREATE TABLE threads(id TEXT,cwd TEXT,title TEXT,updated_at INTEGER,updated_at_ms INTEGER,source TEXT,rollout_path TEXT)")
            for sid, source, updated in (("editor", "vscode", self.now), ("app", "desktop", self.now), ("cli", "cli", self.now), ("child", '{"subagent":{}}', self.now), ("old", "vscode", self.now - 432000001)):
                db.execute("INSERT INTO threads VALUES(?,?,?,?,?,?,?)", (sid, str(self.root / sid), "Task", updated // 1000, updated, source, str(self.root / (sid + ".jsonl"))))
        with sqlite3.connect(str(self.root / "thread_history_1.sqlite")) as db:
            db.execute("CREATE TABLE thread_turns(thread_id TEXT,turn_id TEXT,rollout_ordinal INTEGER,status TEXT,started_at INTEGER,completed_at INTEGER)")
            db.execute("INSERT INTO thread_turns VALUES('editor','turn',1,'inProgress',?,NULL)", ((self.now - 20000) // 1000,))

    def tearDown(self):
        self.temp.cleanup()

    def event(self, kind, payload, at=None):
        at = at or self.now
        return json.dumps({"type": kind, "timestamp": datetime.datetime.fromtimestamp(at / 1000, datetime.timezone.utc).isoformat(), "payload": payload})

    def write_events(self, events):
        (self.root / "editor.jsonl").write_text("\n".join(events) + "\n", encoding="utf-8")

    def test_only_editor_projects_and_five_days(self):
        result = module.load_projects(self.root, self.now)
        self.assertEqual(result["Source"], "vscode")
        self.assertEqual(result["SessionIds"], ["editor"])
        self.assertEqual(len(result["Projects"]), 1)

    def test_rollout_closes_stale_database_running_state(self):
        self.write_events([self.event("event_msg", {"type": "task_started", "turn_id": "turn"}, self.now - 10000), self.event("event_msg", {"type": "task_complete", "turn_id": "turn"})])
        self.assertEqual(module.load_projects(self.root, self.now)["Projects"][0]["Tasks"][0]["State"], "completed")

    def test_streaming_turn_before_database_flush(self):
        self.write_events([self.event("event_msg", {"type": "task_started", "turn_id": "new-turn"})])
        tasks = module.load_projects(self.root, self.now)["Projects"][0]["Tasks"]
        self.assertTrue(any(t["Id"] == "editor:new-turn" and t["State"] == "running" for t in tasks))

    def test_actual_plan_is_available_without_trusted_state_hooks(self):
        self.write_events([self.event("turn_context", {"turn_id": "turn"}), self.event("response_item", {"type": "function_call", "name": "update_plan", "arguments": json.dumps({"plan": [{"status": "completed"}, {"status": "in_progress"}]})})])
        project = module.load_projects(self.root, self.now)["Projects"][0]
        self.assertEqual((project["PlanDone"], project["PlanTotal"]), (1, 2))
        self.assertEqual(project["PlanTaskId"], "editor:turn")

    def test_running_event_never_revives_terminal_database_state(self):
        with sqlite3.connect(str(self.root / "thread_history_1.sqlite")) as db:
            db.execute("UPDATE thread_turns SET status='failed',completed_at=?", ((self.now - 5000) // 1000,))
        self.write_events([self.event("event_msg", {"type": "task_started", "turn_id": "turn"})])
        self.assertEqual(module.load_projects(self.root, self.now)["Projects"][0]["Tasks"][0]["State"], "failed")

    def test_same_folder_has_two_independent_conversations(self):
        with sqlite3.connect(str(self.root / "state_5.sqlite")) as db:
            db.execute("UPDATE threads SET title='分析数据' WHERE id='editor'")
            db.execute("INSERT INTO threads VALUES(?,?,?,?,?,?,?)", ("second", str(self.root / "editor"), "修复桌宠", self.now // 1000, self.now, "vscode", ""))
        with sqlite3.connect(str(self.root / "thread_history_1.sqlite")) as db:
            db.execute("INSERT INTO thread_turns VALUES('second','done',1,'completed',?,?)", ((self.now - 20000) // 1000, self.now // 1000))
        result = module.load_projects(self.root, self.now)
        self.assertEqual(result["Unit"], "conversation")
        conversations = {p["SessionId"]: p for p in result["Projects"]}
        self.assertEqual(len(conversations), 2)
        self.assertEqual(conversations["editor"]["Path"], conversations["second"]["Path"])
        self.assertEqual(conversations["editor"]["Name"], "分析数据")
        self.assertEqual(conversations["second"]["Name"], "修复桌宠")
        self.assertEqual(conversations["editor"]["Tasks"][0]["State"], "running")
        self.assertEqual(conversations["second"]["Tasks"][0]["State"], "completed")

    def test_conversation_without_folder_or_turns_is_visible(self):
        with sqlite3.connect(str(self.root / "state_5.sqlite")) as db:
            db.execute("INSERT INTO threads VALUES(?,?,?,?,?,?,?)", ("empty", "", "新对话", self.now // 1000, self.now, "vscode", ""))
        conversation = next(p for p in module.load_projects(self.root, self.now)["Projects"] if p["SessionId"] == "empty")
        self.assertEqual(conversation["Name"], "新对话")
        self.assertEqual(conversation["Tasks"], [])

    def test_precise_five_day_cutoff_uses_milliseconds(self):
        with sqlite3.connect(str(self.root / "state_5.sqlite")) as db:
            db.execute("UPDATE threads SET updated_at=?,updated_at_ms=? WHERE id='old'", ((self.now - 432000001) // 1000, self.now - 432000001))
        self.assertNotIn("old", module.load_projects(self.root, self.now)["SessionIds"])

    def test_new_numbered_database_is_detected(self):
        (self.root / 'state_5.sqlite').rename(self.root / 'state_6.sqlite')
        (self.root / 'thread_history_1.sqlite').rename(self.root / 'thread_history_2.sqlite')
        with sqlite3.connect(str(self.root / 'state_99.sqlite')) as db:
            db.execute('CREATE TABLE unrelated(value TEXT)')
        self.assertEqual(module.load_projects(self.root, self.now)['SessionIds'], ['editor'])

    def test_rollout_lifecycle_without_history_database(self):
        (self.root / 'thread_history_1.sqlite').unlink()
        self.write_events([self.event('event_msg', {'type': 'task_started', 'turn_id': 'stream'}), self.event('event_msg', {'type': 'task_complete', 'turn_id': 'stream'})])
        tasks = module.load_projects(self.root, self.now)['Projects'][0]['Tasks']
        self.assertEqual([(task['Id'], task['State']) for task in tasks], [('editor:stream', 'completed')])


if __name__ == "__main__":
    unittest.main()
