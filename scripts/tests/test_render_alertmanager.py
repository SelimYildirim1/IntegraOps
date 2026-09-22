import importlib.util
import os
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("render_alertmanager", ROOT / "scripts" / "live" / "render_alertmanager.py")
render = importlib.util.module_from_spec(spec)
spec.loader.exec_module(render)


class RenderAlertmanagerTests(unittest.TestCase):
    def render(self, extra="", webhook=""):
        with tempfile.TemporaryDirectory() as tmp:
            env = Path(tmp) / ".env.live"
            env.write_text(f"OPS_ALERT_EMAIL=ops@example.com\nSMTP_FROM=alerts@example.com\nSMTP_HOST=smtp.example.com\n"
                           f"SMTP_PORT=587\nSMTP_USER=user\nSMTP_PASSWORD=p\"a#ss: word\nLIVE_STATE_DIR={tmp}/state\n"
                           f"OPS_ALERT_WEBHOOK_URL={webhook}\n{extra}")
            sys.argv = ["render_alertmanager.py", str(env)]
            render.main()
            target = Path(tmp) / "state" / "alertmanager.yml"
            return target.read_text(), oct(target.stat().st_mode & 0o777)

    def test_values_are_quoted_and_special_characters_survive(self):
        text, mode = self.render()
        self.assertIn('to: "ops@example.com"', text)
        self.assertIn('smarthost: "smtp.example.com:587"', text)
        self.assertIn('auth_password: "p\\"a#ss: word"', text)
        self.assertIn("require_tls: true", text)
        self.assertNotIn("webhook_configs", text)
        self.assertNotIn("$", text)
        self.assertEqual(mode, "0o644")

    def test_webhook_block_and_starttls_off(self):
        text, _ = self.render(extra="SMTP_STARTTLS=false", webhook="https://hooks.example.com/x")
        self.assertIn('url: "https://hooks.example.com/x"', text)
        self.assertIn("require_tls: false", text)

    def test_insecure_webhook_rejected(self):
        with self.assertRaises(SystemExit):
            self.render(webhook="http://hooks.example.com/x")

    def test_missing_recipient_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            env = Path(tmp) / ".env.live"
            env.write_text(f"SMTP_FROM=a@b\nSMTP_HOST=h\nLIVE_STATE_DIR={tmp}\n")
            sys.argv = ["render_alertmanager.py", str(env)]
            with self.assertRaises(SystemExit):
                render.main()


if __name__ == "__main__":
    unittest.main()
