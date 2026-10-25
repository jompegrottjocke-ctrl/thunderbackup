"""Optional Linux NSS interoperability test. Synthetic credentials only.
Usage: python3 tests/nss_roundtrip.py /path/to/dotnet /path/to/ThunderbirdAccounts.Tests.dll
Uses the same NSS PK11SDR API as Mozilla password storage, with and without a Primary Password.
"""
import base64
import ctypes as C
import json
import pathlib
import subprocess
import sys
import tempfile

class SECItem(C.Structure):
    _fields_ = [("type", C.c_uint), ("data", C.POINTER(C.c_ubyte)), ("len", C.c_uint)]

nss = C.CDLL("libnss3.so")
def api(name, restype, *args):
    function = getattr(nss, name)
    function.restype, function.argtypes = restype, list(args)
    return function
init = api("NSS_InitReadWrite", C.c_int, C.c_char_p)
shutdown = api("NSS_Shutdown", C.c_int)
get_slot = api("PK11_GetInternalKeySlot", C.c_void_p)
free_slot = api("PK11_FreeSlot", None, C.c_void_p)
need_init = api("PK11_NeedUserInit", C.c_int, C.c_void_p)
init_pin = api("PK11_InitPin", C.c_int, C.c_void_p, C.c_char_p, C.c_char_p)
check_password = api("PK11_CheckUserPassword", C.c_int, C.c_void_p, C.c_char_p)
encrypt = api("PK11SDR_Encrypt", C.c_int, C.POINTER(SECItem), C.POINTER(SECItem), C.POINTER(SECItem), C.c_void_p)
decrypt = api("PK11SDR_Decrypt", C.c_int, C.POINTER(SECItem), C.POINTER(SECItem), C.c_void_p)
free_item = api("SECITEM_ZfreeItem", None, C.POINTER(SECItem), C.c_int)

def item(data):
    storage = (C.c_ubyte * len(data)).from_buffer_copy(data)
    return SECItem(0, storage, len(data)), storage

def crypt(data, encode):
    source, keep_alive = item(data)
    result, key_id = SECItem(), SECItem()
    status = encrypt(C.byref(key_id), C.byref(source), C.byref(result), None) if encode else decrypt(C.byref(source), C.byref(result), None)
    assert status == 0, "NSS cryptographic operation failed"
    try:
        return C.string_at(result.data, result.len)
    finally:
        free_item(C.byref(result), 0)

def open_nss(path, password, create):
    assert init(("sql:" + str(path)).encode()) == 0
    slot = get_slot()
    assert slot
    if create and need_init(slot):
        assert init_pin(slot, None, password.encode()) == 0
    if password and not create:
        assert check_password(slot, b"wrong-primary-password") != 0, "Primary Password protection lost"
    assert check_password(slot, password.encode()) == 0
    free_slot(slot)

for primary in ("", "synthetic-primary-password"):
    with tempfile.TemporaryDirectory(prefix="tbaccounts-nss-") as directory:
        root = pathlib.Path(directory)
        profile = root / "source"
        profile.mkdir()
        settings = {
            "mail.accountmanager.accounts": "account1",
            "mail.accountmanager.defaultaccount": "account1",
            "mail.account.account1.server": "server1",
            "mail.account.account1.identities": "id1",
            "mail.server.server1.type": "imap",
            "mail.server.server1.hostname": "imap.example.invalid",
            "mail.server.server1.name": "Synthetic NSS account",
            "mail.server.server1.userName": "synthetic-user",
            "mail.identity.id1.useremail": "user@example.invalid",
            "mail.identity.id1.smtpServer": "smtp1",
            "mail.smtpservers": "smtp1",
            "mail.smtp.defaultserver": "smtp1",
            "mail.smtpserver.smtp1.hostname": "smtp.example.invalid",
        }
        (profile / "prefs.js").write_text("\n".join(f"user_pref({json.dumps(k)}, {json.dumps(v)});" for k, v in settings.items()))
        open_nss(profile, primary, True)
        try:
            username = base64.b64encode(crypt(b"synthetic-user", True)).decode()
            password = base64.b64encode(crypt(b"synthetic-mail-password", True)).decode()
        finally:
            assert shutdown() == 0
        (profile / "logins.json").write_text(json.dumps({"version": 3, "nextId": 2, "logins": [{
            "id": 1, "hostname": "imap://imap.example.invalid", "httpRealm": "imap://imap.example.invalid",
            "formSubmitURL": None, "usernameField": "", "passwordField": "", "encryptedUsername": username,
            "encryptedPassword": password, "guid": "{synthetic-fixture}", "encType": 1,
            "timeCreated": 0, "timeLastUsed": 0, "timePasswordChanged": 0, "timesUsed": 0
        }]}))
        run = subprocess.run([sys.argv[1], sys.argv[2], "--nss-roundtrip", str(profile), str(root / "destination")], text=True, capture_output=True, check=True)
        restored = pathlib.Path(run.stdout.strip().splitlines()[-1])
        logins = json.loads((restored / "logins.json").read_text())["logins"][0]
        open_nss(restored, primary, False)
        try:
            assert crypt(base64.b64decode(logins["encryptedUsername"]), False) == b"synthetic-user"
            assert crypt(base64.b64decode(logins["encryptedPassword"]), False) == b"synthetic-mail-password"
        finally:
            assert shutdown() == 0
        print("PASS NSS encrypted backup -> restore -> decrypt" + (" with Primary Password" if primary else " without Primary Password"))
