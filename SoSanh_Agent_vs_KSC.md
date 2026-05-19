# So sanh Agent thu thap hien tai vs Kaspersky Security Center (KSC/KAS)

Ngay cap nhat: 2026-05-19

## 1) Muc tieu tai lieu

Tai lieu nay giup quyet dinh nen:
- Phat trien them agent thu thap local tren endpoint
- Khai thac du lieu tu KSC/KAS master server
- Hoac ket hop ca hai

## 2) Tom tat nhanh

Ket luan de xuat: chon mo hinh Hybrid (2 nguon du lieu).

- Agent local manh o do sau cau hinh he dieu hanh, hardening, persistence, network exposure.
- KSC manh o quan tri tap trung, policy assignment/compliance, task orchestration, su kien bao mat toan he thong.
- Neu chi dung 1 ben se tao diem mu:
  - Chi Agent: thieu goc nhin quan tri tap trung va policy tu KSC.
  - Chi KSC: thieu nhieu chi tiet hardening thuc te tren OS ma endpoint collector dang co.

## 3) Du lieu Agent hien tai dang thu duoc

Theo InfoKiemTra.md, Agent da co 21 collector, gom cac nhom lon:

- Identity/Domain: GPO, ActiveDirectory, LAPS, LocalAccounts
- OS Security baseline: SecurityPolicy, RegistryAudit, Defender, BitLocker, Patch
- Exposure/Persistence: Firewall + ListeningPorts + RiskyPorts, Services, ScheduledTasks, StartupEntries, SharedFolders, RemoteAccess, WiFi
- Endpoint AV overview: EndpointProtection (SecurityCenter2 + registry Kaspersky local)

Gia tri noi bat:

- Do sau endpoint rat cao (nhieu key registry va command low-level)
- Co nhieu rule co the map truc tiep sang violation (critical/high)
- Co kha nang phat hien lech cau hinh thuc te tai may (ground truth)

Gioi han:

- Chua lay du lieu quan tri tap trung tu KSC master
- Chua biet host dang thuoc group/policy nao trong KSC
- Chua co lich su task, event, quarantine, license tu KSC

## 4) Du lieu KSC/KAS thuong co (master server)

Thong thuong KSC cho phep (qua console/API) cac nhom:

- Device inventory tap trung: host managed/unmanaged, last seen, admin group
- Policy management: policy dang ap dung, inheritance, lock, profile
- Task orchestration: deploy/update/scan task status, scheduler, fail/success history
- Security events: malware detection, action taken, quarantine/rollback thong ke
- Operational: update source, distribution point, module health, license state
- Reporting toan fleet: trend, top risk hosts, compliance theo group

Gia tri noi bat:

- Goc nhin quan tri tap trung va kha nang dieu phoi
- Dung de audit compliance theo don vi/nhom
- Tot cho SOC/ops dashboard va workflow xu ly su co

Gioi han:

- Khong phai luc nao cung co do sau OS hardening nhu collector local
- Co the co do tre dong bo hoac mismatch voi trang thai thuc te tai endpoint

## 5) Ma tran so sanh truc tiep

| Tieu chi | Agent local hien tai | KSC/KAS master |
|---|---|---|
| Do sau cau hinh OS | Rat cao | Trung binh |
| Kiem tra hardening (registry, auditpol, secedit) | Rat tot | Han che/khong day du |
| Trang thai AV/FW Kaspersky | Co (local) | Co (tap trung, quan tri) |
| Policy assignment va inheritance | Khong | Rat tot |
| Task deployment/scan/update lich su | Khong | Rat tot |
| Security event tap trung toan fleet | Han che | Rat tot |
| Quarantine/incident lifecycle | Khong/han che | Rat tot |
| Fleet-level compliance reporting | Han che | Rat tot |
| Do tin cay trang thai thuc te tren may | Rat cao | Cao nhung co do tre |
| Do phuc tap tich hop ban dau | Trung binh | Trung binh-cao (API, auth, mapping) |

## 6) Khoang trong hien tai can bo sung

Neu muc tieu la thay the phan lon bao cao KSC, he thong hien tai con thieu:

- KSC host identity mapping (host id/group/path)
- Policy snapshot va policy compliance theo host
- Task result timeline (scan/update/deploy)
- Security event stream (detection/action/quarantine)
- License va operational health

## 7) Nen phat trien them Agent hay khai thac KAS?

Khuyen nghi theo muc tieu:

### Truong hop A: Uu tien hardening va soat cau hinh may

Nen dau tu them Agent neu ban can:
- Them collector cho deep OS artifact
- Rule custom theo tieu chuan noi bo (CIS, policy noi bo)
- Phat hien persistence va misconfig sat voi may thuc te

### Truong hop B: Uu tien quan tri tap trung va van hanh SOC

Nen khai thac KSC/KAS neu ban can:
- Bao cao toan fleet theo group
- Theo doi policy drift tap trung
- Quan ly task, incident, quarantine lifecycle

### Truong hop C: Muc tieu day du nhat (khuyen nghi)

Hybrid:
- Agent = Ground truth endpoint + hardening depth
- KSC = Governance, operations, fleet security events
- Backend = Correlation engine hop nhat 2 nguon

## 8) De xuat lo trinh trien khai thuc te (4 giai doan)

### Giai doan 1 (ngan han, 1-2 sprint)

- Giu nguyen collector hien tai
- Chuan hoa host identity trong payload (hostname/FQDN/domain + endpoint id)
- Bo sung cac truong can thiet de map voi KSC host

Deliverable:
- Bang map host identity 1-1 giua Agent va KSC

### Giai doan 2 (2-3 sprint)

- Xay KSC connector o backend (worker dong bo dinh ky)
- Nap du lieu: inventory, policy assignment, task status, security events co ban

Deliverable:
- Bang du lieu KSC_* trong backend + dong bo incremental

### Giai doan 3 (2 sprint)

- Xay correlation rules:
  - KSC expected policy vs Agent actual state
  - KSC task expected vs endpoint evidence
- Tao violation moi theo mismatch 2 nguon

Deliverable:
- Compliance score hop nhat theo host

### Giai doan 4 (1-2 sprint)

- Dashboard va report dieu hanh:
  - Fleet risk heatmap
  - Top host mismatch
  - Policy drift by group

Deliverable:
- View quan tri va canh bao uu tien theo tac dong

## 9) Quyet dinh de nghi

Neu ban dang phan van chi dau tu 1 ben, de xuat:

1. Khong bo Agent hien tai vi dang tao gia tri hardening rat cao.
2. Dau tu tiep theo vao KSC integration de bo sung governance gap.
3. Dat muc tieu Hybrid la dich den chinh.

## 10) Checklist ra quyet dinh nhanh

Tra loi 3 cau hoi sau:

1. Ban co can bao cao fleet-level cho lanh dao/SOC theo group va trend khong?
2. Ban co can doi chieu policy trung tam voi cau hinh thuc te tren may khong?
3. Ban co can su kien incident/quarantine/task lifecycle de van hanh hang ngay khong?

Neu >= 2 cau tra loi la Co: uu tien khai thac KSC ngay, song song giu va toi uu Agent.

---

Ghi chu ky thuat:
- Tai lieu nay danh gia theo pham vi du lieu hien co trong project va mo hinh thong tin KSC thong dung.
- Can xac nhan them danh sach endpoint API/chuc nang KSC phien ban dang dung de chot backlog chinh xac.