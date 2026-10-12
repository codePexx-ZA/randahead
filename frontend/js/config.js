// App settings: API address, demo account hints, account rules and the industry list
export const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
export const MIN_PASSWORD_LENGTH = 8;

export const CONFIG = {
  API_BASE: "http://localhost:5080/api",
  SHOW_DEMO_ACCOUNTS: true,
};

export const INDUSTRIES = [
  "Retail",
  "Wholesale",
  "Manufacturing",
  "Construction",
  "Hospitality and tourism",
  "Transport and logistics",
  "Professional services",
  "Agriculture",
  "Health",
  "Education",
  "Information technology",
  "Personal services",
  "Other",
];
