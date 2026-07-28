#!/usr/bin/env python3
"""Procurement Approval Golden Sample — Python E2E Tests.

Uses only stdlib (urllib, json, unittest). No external dependencies.

Usage:
    # Server must be running at BASE_URL (default http://localhost:5000)
    python3 test_procurement_e2e.py
"""

import json
import os
import unittest
import urllib.request
import urllib.error

BASE_URL = os.environ.get("PROCUREMENT_BASE_URL", "http://localhost:5000")


def _identity_headers(user="requester-001", roles="procurement-requester",
                      tenant="tenant-e2e") -> dict[str, str]:
    return {
        "X-Sample-Tenant": tenant,
        "X-Sample-User": user,
        "X-Sample-Roles": roles,
    }


def _post(path: str, body: dict, *, user="requester-001",
          roles="procurement-requester", tenant="tenant-e2e") -> tuple[int, dict | None]:
    data = json.dumps(body).encode()
    headers = _identity_headers(user, roles, tenant)
    headers["Content-Type"] = "application/json"
    req = urllib.request.Request(
        f"{BASE_URL}{path}", data=data, method="POST",
        headers=headers,
    )
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, json.loads(resp.read())
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read())
        except Exception:
            return e.code, None


def _get(path: str, *, user="requester-001", roles="procurement-requester",
         tenant="tenant-e2e") -> tuple[int, dict | None]:
    req = urllib.request.Request(
        f"{BASE_URL}{path}", method="GET",
        headers=_identity_headers(user, roles, tenant))
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, json.loads(resp.read())
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read())
        except Exception:
            return e.code, None


def _get_text(path: str) -> tuple[int, str]:
    req = urllib.request.Request(
        f"{BASE_URL}{path}", method="GET",
        headers=_identity_headers())
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, resp.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()


def _submit(title="Test", amount=5000, currency="USD",
            category="General",
            description="") -> tuple[int, dict | None]:
    return _post("/api/procurement/requests", {
        "title": title, "amount": amount, "currency": currency,
        "category": category, "description": description,
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
            {"comment": "Budget approved"},
            user="cfo-001", roles="procurement-manager")
        self.assertEqual(status, 200)
        self.assertIsNotNone(data)
        self.assertEqual(data["status"], "Approved")
        self.assertEqual(data["approverId"], "cfo-001")

    def test_07_reject_high_value_request(self):
        _, submit_data = _submit(title="Expensive Item", amount=50000)
        request_id = submit_data["requestId"]
        self.assertEqual(submit_data["status"], "PendingApproval")

        status, data = _post(
            f"/api/procurement/requests/{request_id}/reject",
            {"reason": "Over budget"},
            user="cfo-002", roles="procurement-manager")
        self.assertEqual(status, 200)
        self.assertIsNotNone(data)
        self.assertEqual(data["status"], "Rejected")
        self.assertEqual(data["approverId"], "cfo-002")

    def test_08_get_nonexistent_request(self):
        fake_id = "00000000-0000-0000-0000-000000000000"
        status, _ = _get(f"/api/procurement/requests/{fake_id}")
        self.assertEqual(status, 404)

    def test_09_full_workflow_submit_approve_get(self):
        _, submit_data = _submit(title="Workflow Test", amount=20000)
        request_id = submit_data["requestId"]
        self.assertEqual(submit_data["status"], "PendingApproval")

        approve_status, approve_data = _post(
            f"/api/procurement/requests/{request_id}/approve",
            {"comment": "OK"},
            user="cfo-003", roles="procurement-manager")
        self.assertEqual(approve_status, 200)
        self.assertEqual(approve_data["status"], "Approved")

        status, get_data = _get(f"/api/procurement/requests/{request_id}")
        self.assertEqual(status, 200)
        self.assertEqual(get_data["status"], "Approved")
        self.assertEqual(get_data["title"], "Workflow Test")

    def test_10_compatibility_projection_submit(self):
        status, data = _get(
            "/api/procurement/submit"
            "?title=CompatTest&amount=3000&currency=USD"
            "&category=General")
        self.assertEqual(status, 200)
        self.assertIsNotNone(data)
        self.assertIn("data", data)

    def test_11_compatibility_projection_approve(self):
        _, submit_data = _submit(title="Compat Approve", amount=40000)
        request_id = submit_data["requestId"]

        status, data = _get(
            f"/api/procurement/approve"
            f"?requestId={request_id}&comment=OK",
            user="cfo-004", roles="procurement-manager")
        self.assertEqual(status, 200)
        self.assertIsNotNone(data)
        self.assertIn("data", data)
        get_status, entity = _get(f"/api/procurement/requests/{request_id}")
        self.assertEqual(get_status, 200)
        self.assertEqual(entity["status"], "Approved")
        self.assertEqual(entity["approverId"], "cfo-004")


if __name__ == "__main__":
    unittest.main()
