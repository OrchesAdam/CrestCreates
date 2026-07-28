#!/usr/bin/env python3
"""Procurement Approval Golden Sample — Python E2E Tests.

Uses only stdlib (urllib, json, unittest). No external dependencies.

Usage:
    # Server must be running at BASE_URL (default http://localhost:5000)
    python3 test_procurement_e2e.py
"""

import json
import unittest
import urllib.request
import urllib.error

BASE_URL = "http://localhost:5000"


def _post(path: str, body: dict) -> tuple[int, dict | None]:
    data = json.dumps(body).encode()
    req = urllib.request.Request(
        f"{BASE_URL}{path}", data=data, method="POST",
        headers={"Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, json.loads(resp.read())
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read())
        except Exception:
            return e.code, None


def _get(path: str) -> tuple[int, dict | None]:
    req = urllib.request.Request(f"{BASE_URL}{path}", method="GET")
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, json.loads(resp.read())
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read())
        except Exception:
            return e.code, None


def _get_text(path: str) -> tuple[int, str]:
    req = urllib.request.Request(f"{BASE_URL}{path}", method="GET")
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, resp.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()


def _submit(title="Test", amount=5000, currency="USD",
            requester_id="user-001", category="General",
            description="") -> tuple[int, dict | None]:
    return _post("/api/procurement/requests", {
        "title": title, "amount": amount, "currency": currency,
        "requesterId": requester_id, "category": category,
        "description": description,
    })


class ProcurementE2ETests(unittest.TestCase):

    def test_01_openapi_json_is_valid_and_contains_procurement(self):
        status, data = _get("/openapi/v1.json")
        self.assertEqual(status, 200)
        self.assertIn("paths", data)
        paths = list(data["paths"].keys())
        self.assertTrue(any("procurement" in p for p in paths),
                        f"No procurement paths in {paths}")

    def test_02_scalar_ui_loads(self):
        status, html = _get_text("/scalar")
        self.assertEqual(status, 200)
        self.assertIn("scalar", html.lower())

    def test_03_submit_low_value_auto_approved(self):
        status, data = _submit(title="Office Supplies", amount=5000)
        self.assertEqual(status, 201)
        self.assertIsNotNone(data)
        self.assertEqual(data["status"], "Approved")
        self.assertFalse(data["requiresApproval"])

    def test_04_submit_high_value_requires_approval(self):
        status, data = _submit(title="Server Rack", amount=15000)
        self.assertEqual(status, 201)
        self.assertIsNotNone(data)
        self.assertEqual(data["status"], "PendingApproval")
        self.assertTrue(data["requiresApproval"])

    def test_05_submit_then_get_request(self):
        _, submit_data = _submit(
            title="Cloud Services", amount=8000,
            description="Annual cloud subscription")
        request_id = submit_data["requestId"]
        status, data = _get(f"/api/procurement/requests/{request_id}")
        self.assertEqual(status, 200)
        self.assertIsNotNone(data)
        self.assertEqual(data["title"], "Cloud Services")
        self.assertEqual(data["amount"], 8000)
        self.assertEqual(data["currency"], "USD")

    def test_06_approve_high_value_request(self):
        _, submit_data = _submit(title="Big Order", amount=25000)
        request_id = submit_data["requestId"]
        self.assertEqual(submit_data["status"], "PendingApproval")

        status, data = _post(
            f"/api/procurement/requests/{request_id}/approve",
            {"approverId": "cfo-001", "comment": "Budget approved"})
        self.assertIn(status, (200, 201))
        self.assertIsNotNone(data)
        self.assertEqual(data["status"], "Approved")
        self.assertEqual(data["approverId"], "cfo-001")

    def test_07_reject_high_value_request(self):
        _, submit_data = _submit(title="Expensive Item", amount=50000)
        request_id = submit_data["requestId"]
        self.assertEqual(submit_data["status"], "PendingApproval")

        status, data = _post(
            f"/api/procurement/requests/{request_id}/reject",
            {"approverId": "cfo-002", "reason": "Over budget"})
        self.assertIn(status, (200, 201))
        self.assertIsNotNone(data)
        self.assertEqual(data["status"], "Rejected")
        self.assertEqual(data["approverId"], "cfo-002")

    def test_08_get_nonexistent_request(self):
        fake_id = "00000000-0000-0000-0000-000000000000"
        status, data = _get(f"/api/procurement/requests/{fake_id}")
        self.assertIn(status, (200, 404))
        if status == 200 and data:
            self.assertEqual(data.get("status"), "NotFound")

    def test_09_full_workflow_submit_approve_get(self):
        _, submit_data = _submit(title="Workflow Test", amount=20000)
        request_id = submit_data["requestId"]
        self.assertEqual(submit_data["status"], "PendingApproval")

        _, approve_data = _post(
            f"/api/procurement/requests/{request_id}/approve",
            {"approverId": "cfo-003", "comment": "OK"})
        self.assertEqual(approve_data["status"], "Approved")

        status, get_data = _get(f"/api/procurement/requests/{request_id}")
        self.assertEqual(status, 200)
        self.assertEqual(get_data["status"], "Approved")
        self.assertEqual(get_data["title"], "Workflow Test")

    def test_10_compatibility_projection_submit(self):
        status, data = _get(
            "/api/procurement/submit"
            "?title=CompatTest&amount=3000&currency=USD"
            "&requesterId=user-002&category=General")
        self.assertEqual(status, 200)
        self.assertIsNotNone(data)
        self.assertIn("data", data)

    def test_11_compatibility_projection_approve(self):
        _, submit_data = _submit(title="Compat Approve", amount=40000)
        request_id = submit_data["requestId"]

        status, data = _get(
            f"/api/procurement/approve"
            f"?requestId={request_id}&approverId=cfo-004&comment=OK")
        self.assertEqual(status, 200)
        self.assertIsNotNone(data)
        self.assertIn("data", data)


if __name__ == "__main__":
    unittest.main()
