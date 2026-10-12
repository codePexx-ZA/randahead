// Roles: labels, home sections, duties, demo accounts and which role a locked action needs
export const ROLES = ["submitter", "approver", "decision_maker"];

export const ROLE_LABELS = { submitter: "Submitter", approver: "Approver", decision_maker: "Decision Maker" };

export const ROLE_HOME = { submitter: "upload", approver: "approvals", decision_maker: "dashboard" };

export const ROLE_DUTIES = {
  submitter: "Uploads income statements and fixes rejected ones.",
  approver: "Approves or rejects uploads and budgets, tunes warning rules and manages Submitters.",
  decision_maker: "Chooses the budget, sets KPI targets and warning rules, manages users and the business details.",
};

export const DEMO_ACCOUNTS = { submitter: "submitter@demo.co.za", approver: "approver@demo.co.za", decision_maker: "owner@demo.co.za" };

export const ACTION_ROLE = {
  upload: "submitter",
  chooseBudget: "decision_maker",
  decide: "approver",
  editTargets: "decision_maker",
  editRules: "approver",
  manageUsers: "decision_maker",
  editBusiness: "decision_maker",
  viewReports: "decision_maker",
};

export function roleLabel(user) {
  return user.isAdmin ? "Admin" : ROLE_LABELS[user.role] || user.role;
}
