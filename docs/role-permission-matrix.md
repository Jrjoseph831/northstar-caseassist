# Northstar CaseAssist — Role and Permission Matrix

This matrix describes the currently implemented API authorization boundary.
The private Sites BFF maps the selected fictional persona to
`X-Northstar-Demo-User`; the Azure API recognizes only seeded synthetic users
and enforces permissions independently of frontend visibility. Microsoft Entra
ID app roles are the documented production replacement.

| Action | Caseworker | Reviewer | Administrator | Anonymous citizen demo |
|---|---:|---:|---:|---:|
| Submit citizen-demo application | Yes | Yes | Yes | Yes |
| Submit employee-assisted application | Yes | No | Yes | No |
| Read application | Own submissions | No | All | No |
| Convert application to case | Yes | No | Yes | No |
| List cases | Assigned only | No | All | No |
| Read case | Assigned only | No | All | No |
| Reassign case | No | No | Yes | No |
| Add case note | Assigned only | No | All | No |
| Update case status | Assigned only | No | All | No |
| Create CaseAssist request | Assigned only | No | No | No |
| View review item | Own submissions | Assigned only | All | No |
| Approve/return/reject review | No | Assigned only; never own submission | Inspect only | No |
| Upload/list case documents | Assigned only | No | All | No |
| Run control evaluation | No | No | Yes | No |
| Read audit events | No | No | Yes | No |

Every denied endpoint access writes a safe audit event containing the synthetic
actor identifier or `anonymous`, role, action, resource identifier, denial
reason, and correlation ID. It does not contain applicant data or credentials.

A reviewer never gains general case-management access merely from holding the
Reviewer role. Approval creates only an approved draft case note; it never
changes eligibility, authorizes payment, closes a case, or contacts an applicant.
