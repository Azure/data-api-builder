# Authorization

Data API builder uses a role-based authorization workflow.

## Roles

Roles, with the exception of the system roles described below, are not pre-defined and are inferred from the claims found in the incoming request.




### System Roles

There are two system roles:

- `anonymous`: all non-authenticated requests will be assigned to this role
- `authenticated`: all authenticated requests will be assigned to this role

### User Roles


## 